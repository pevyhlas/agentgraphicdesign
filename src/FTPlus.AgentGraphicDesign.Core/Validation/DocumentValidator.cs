using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Layout;
using FTPlus.AgentGraphicDesign.Core.Ordering;

namespace FTPlus.AgentGraphicDesign.Core.Validation;

public sealed class ValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public List<ValidationError> Errors { get; } = [];
    public List<ValidationWarning> Warnings { get; } = [];
}

public sealed record ValidationError(string Code, string Message, string? Path = null);
public sealed record ValidationWarning(string Code, string Message, string? Path = null);

public static class DocumentValidator
{
    public const int MaxRasterDimension = 32768;

    public static ValidationResult Validate(Document document) => Validate(document, ValidationOptions.Default);

    public static ValidationResult Validate(Document document, ValidationOptions? options)
    {
        options ??= ValidationOptions.Default;
        if (options.Profile is not (ValidationProfile.Default or ValidationProfile.WebIcon))
            throw new ArgumentOutOfRangeException(nameof(options), options.Profile, "Unknown validation profile");

        var result = new ValidationResult();
        var tokenDiagnostics = new TokenDiagnosticBudget(result);

        if (document.Id == Guid.Empty)
        {
            result.Errors.Add(new ValidationError("DOC001", "Document ID is empty", "id"));
        }

        if (string.IsNullOrWhiteSpace(document.Name))
        {
            result.Errors.Add(new ValidationError("DOC002", "Document name is empty or whitespace", "name"));
        }

        if (document.Version < 1)
        {
            result.Errors.Add(new ValidationError("DOC003", "Document version must be >= 1", "version"));
        }

        if (document.Version > Document.SupportedVersion)
        {
            result.Warnings.Add(new ValidationWarning(
                "DOC005",
                $"Document version {document.Version} is newer than supported version {Document.SupportedVersion}; some features may not be preserved",
                "version"));
        }

        if (document.Root is not null && document.Id == document.Root.Id)
            result.Errors.Add(new ValidationError("DOC006", "Document and root IDs must be distinct", "root.id"));

        if (document.Revn < 1)
        {
            result.Errors.Add(new ValidationError("DOC004", "Document revision must be >= 1", "revn"));
        }

        if (document.Canvas is null)
            result.Errors.Add(new ValidationError("DOC007", "Document canvas is null", "canvas"));
        else
        {
            ValidateCanvas(document.Canvas, result);
            if (options.Profile == ValidationProfile.WebIcon)
                ValidateWebIconBackground(
                    options.BackgroundOverride ?? document.Canvas.Background,
                    options.BackgroundOverride is null ? "canvas.background" : "export.background",
                    result);
        }
        ValidateTokens(document, result, tokenDiagnostics);
        if (document.Root is null)
            result.Errors.Add(new ValidationError("DOC008", "Document root is null", "root"));
        else
            ValidateNode(document, document.Root, "root", result, new HashSet<Guid>(), options.Profile, tokenDiagnostics);

        if (document.Canvas is not null && document.Root is not null)
        {
            var violations = DocumentLimits.FindPersistedStringViolations(document)
                .Take(DocumentLimits.MaxPersistedStringDiagnostics + 1)
                .ToArray();
            foreach (var violation in violations.Take(DocumentLimits.MaxPersistedStringDiagnostics))
            {
                result.Errors.Add(new ValidationError(
                    "RESOURCE001",
                    $"String exceeds maximum length ({violation.MaximumLength})",
                    violation.Path));
            }
            if (violations.Length > DocumentLimits.MaxPersistedStringDiagnostics)
            {
                result.Errors.Add(new ValidationError(
                    "RESOURCE004",
                    $"Persisted string diagnostics exceed maximum ({DocumentLimits.MaxPersistedStringDiagnostics})"));
            }
        }

        return result;
    }

    private static void ValidateWebIconBackground(string? background, string path, ValidationResult result)
    {
        if (background is not null && !DocumentLimits.IsFullyTransparentColor(background))
        {
            result.Errors.Add(new ValidationError(
                WebIconValidationCodes.Background,
                "web-icon profile requires a fully transparent canvas background",
                path));
        }
    }

    private static void ValidateCanvas(Canvas canvas, ValidationResult result)
    {
        if (!double.IsFinite(canvas.Width))
        {
            result.Errors.Add(new ValidationError("CANVAS004", "Canvas width is not a finite number", "canvas.width"));
        }
        else if (canvas.Width <= 0)
        {
            result.Errors.Add(new ValidationError("CANVAS001", "Canvas width must be > 0", "canvas.width"));
        }
        else if (canvas.Width > MaxRasterDimension)
        {
            result.Errors.Add(new ValidationError("CANVAS005", $"Canvas width exceeds maximum raster dimension ({MaxRasterDimension})", "canvas.width"));
        }
        else if (canvas.Width > 16384)
        {
            result.Warnings.Add(new ValidationWarning("CANVAS003", "Canvas width exceeds 16384; may cause performance issues", "canvas.width"));
        }

        if (!double.IsFinite(canvas.Height))
        {
            result.Errors.Add(new ValidationError("CANVAS006", "Canvas height is not a finite number", "canvas.height"));
        }
        else if (canvas.Height <= 0)
        {
            result.Errors.Add(new ValidationError("CANVAS002", "Canvas height must be > 0", "canvas.height"));
        }
        else if (canvas.Height > MaxRasterDimension)
        {
            result.Errors.Add(new ValidationError("CANVAS007", $"Canvas height exceeds maximum raster dimension ({MaxRasterDimension})", "canvas.height"));
        }
        else if (canvas.Height > 16384)
        {
            result.Warnings.Add(new ValidationWarning("CANVAS008", "Canvas height exceeds 16384; may cause performance issues", "canvas.height"));
        }

        if (canvas.Background is not null && !DocumentLimits.IsValidColor(canvas.Background))
            result.Errors.Add(new ValidationError("CANVAS009", "Invalid canvas background color", "canvas.background"));
    }

    private static void ValidateNode(
        Document document,
        Node node,
        string path,
        ValidationResult result,
        HashSet<Guid> seenIds,
        ValidationProfile profile,
        TokenDiagnosticBudget tokenDiagnostics)
    {
        if (node.Id == Guid.Empty)
        {
            result.Errors.Add(new ValidationError("NODE001", "Node ID is empty", $"{path}.id"));
        }
        else if (!seenIds.Add(node.Id))
        {
            result.Errors.Add(new ValidationError("NODE002", $"Duplicate node ID: {node.Id}", $"{path}.id"));
        }

        if (string.IsNullOrEmpty(node.Order))
        {
            result.Errors.Add(new ValidationError("NODE003", "Node order is empty", $"{path}.order"));
        }
        else if (node.Order.Length > DocumentLimits.MaxNodeOrderLength || !FractionalIndex.IsValid(node.Order))
        {
            result.Errors.Add(new ValidationError("NODE009", "Node order must use the bounded base-62 alphabet", $"{path}.order"));
        }

        if (node.Transform is not null)
        {
            ValidateFinite(node.Transform.X, $"{path}.transform.x", "NODE004", "Node X coordinate", result);
            ValidateFinite(node.Transform.Y, $"{path}.transform.y", "NODE005", "Node Y coordinate", result);
            ValidateFinite(node.Transform.Rotation, $"{path}.transform.rotation", "NODE006", "Node rotation", result);
            ValidateFinite(node.Transform.ScaleX, $"{path}.transform.scaleX", "NODE007", "Node scaleX", result);
            ValidateFinite(node.Transform.ScaleY, $"{path}.transform.scaleY", "NODE008", "Node scaleY", result);
        }

        ValidateStyle(node.Style, path, result);
        ValidateNodeTokenReferences(document, node, path, tokenDiagnostics);
        if (profile == ValidationProfile.WebIcon)
            ValidateWebIconNode(node, path, result);

        switch (node)
        {
            case FrameNode frame:
                ValidateFrameNode(document, frame, path, result, seenIds, profile, tokenDiagnostics);
                break;
            case GroupNode group:
                ValidateGroupNode(document, group, path, result, seenIds, profile, tokenDiagnostics);
                break;
            case RectangleNode rect:
                ValidateRectangleNode(rect, path, result);
                break;
            case EllipseNode ellipse:
                ValidateEllipseNode(ellipse, path, result);
                break;
            case PathNode pathNode:
                ValidatePathNode(pathNode, path, result);
                break;
            case TextNode text:
                ValidateTextNode(text, path, result);
                break;
            case LineNode line:
                ValidateLineNode(line, path, result);
                break;
            case PolygonNode polygon:
                ValidatePolygonNode(polygon, path, result);
                break;
            case ImageNode image:
                ValidateImageNode(image, path, result);
                break;
        }
    }

    private static void ValidateWebIconNode(Node node, string path, ValidationResult result)
    {
        var error = node switch
        {
            TextNode => new ValidationError(
                WebIconValidationCodes.TextNode,
                "web-icon profile does not allow text nodes",
                $"{path}.type"),
            ImageNode => new ValidationError(
                WebIconValidationCodes.ImageNode,
                "web-icon profile does not allow image nodes or image resources",
                $"{path}.type"),
            UnknownNode => new ValidationError(
                WebIconValidationCodes.UnknownNode,
                "web-icon profile does not allow unknown node types",
                $"{path}.type"),
            FrameNode or GroupNode or RectangleNode or EllipseNode or PathNode or LineNode or PolygonNode => null,
            _ => new ValidationError(
                WebIconValidationCodes.UnknownNode,
                "web-icon profile does not allow unknown node types",
                $"{path}.type")
        };

        if (error is not null)
            result.Errors.Add(error);
    }

    private static readonly HashSet<string> SupportedBlendModes =
    [
        "normal", "multiply", "screen", "overlay", "darken", "lighten",
        "color-dodge", "color-burn", "hard-light", "soft-light", "difference",
        "exclusion", "hue", "saturation", "color", "luminosity"
    ];

    private static void ValidateStyle(Style? style, string path, ValidationResult result)
    {
        if (style is null) return;

        if (style.Opacity is < 0 or > 1)
        {
            result.Errors.Add(new ValidationError("STYLE001", "Opacity must be between 0 and 1", $"{path}.style.opacity"));
        }

        if (style.Fill?.Color is not null && !IsValidColor(style.Fill.Color))
        {
            result.Errors.Add(new ValidationError("STYLE002", $"Invalid fill color format: {style.Fill.Color}", $"{path}.style.fill.color"));
        }

        ValidateGradient(style.Fill?.Gradient, $"{path}.style.fill.gradient", result);

        if (style.Stroke?.Color is not null && !IsValidColor(style.Stroke.Color))
        {
            result.Errors.Add(new ValidationError("STYLE003", $"Invalid stroke color format: {style.Stroke.Color}", $"{path}.style.stroke.color"));
        }

        if (style.Stroke?.Width is { } strokeWidth)
        {
            if (!double.IsFinite(strokeWidth))
            {
                result.Errors.Add(new ValidationError("STYLE008", "Stroke width is not a finite number", $"{path}.style.stroke.width"));
            }
            else if (strokeWidth < 0)
            {
                result.Errors.Add(new ValidationError("STYLE004", "Stroke width cannot be negative", $"{path}.style.stroke.width"));
            }
        }

        if (style.Stroke?.MiterLimit is { } miterLimit)
        {
            if (!double.IsFinite(miterLimit))
            {
                result.Errors.Add(new ValidationError("STYLE009", "Stroke miter limit is not a finite number", $"{path}.style.stroke.miterLimit"));
            }
            else if (miterLimit < 1)
            {
                result.Errors.Add(new ValidationError("STYLE005", "Stroke miter limit must be >= 1", $"{path}.style.stroke.miterLimit"));
            }
        }

        if (style.Stroke?.DashArray is { } dashArray)
        {
            for (int i = 0; i < dashArray.Length; i++)
            {
                if (!double.IsFinite(dashArray[i]))
                {
                    result.Errors.Add(new ValidationError("STYLE010", "Dash array value is not a finite number", $"{path}.style.stroke.dashArray[{i}]"));
                }
                else if (dashArray[i] < 0)
                {
                    result.Errors.Add(new ValidationError("STYLE006", "Dash array values cannot be negative", $"{path}.style.stroke.dashArray[{i}]"));
                }
            }
        }

        if (style.Stroke?.DashOffset is { } dashOffset && !double.IsFinite(dashOffset))
        {
            result.Errors.Add(new ValidationError("STYLE011", "Dash offset is not a finite number", $"{path}.style.stroke.dashOffset"));
        }

        if (style.BlendMode is not null && !SupportedBlendModes.Contains(style.BlendMode.ToLowerInvariant()))
        {
            result.Warnings.Add(new ValidationWarning("STYLE007", $"Unsupported blend mode: {style.BlendMode}; supported: {string.Join(", ", SupportedBlendModes)}", $"{path}.style.blendMode"));
        }

        if (!FillRules.IsValid(style.FillRule))
        {
            result.Errors.Add(new ValidationError("STYLE012", "fillRule must be nonzero or evenodd", $"{path}.style.fillRule"));
        }
    }

    private static void ValidateGradient(Gradient? gradient, string path, ValidationResult result)
    {
        if (gradient is null) return;

        if (gradient.Stops.Count < 2)
        {
            result.Warnings.Add(new ValidationWarning("GRADIENT001", "Gradient should have at least 2 stops", $"{path}.stops"));
        }

        for (int i = 0; i < gradient.Stops.Count; i++)
        {
            var stop = gradient.Stops[i];
            if (!double.IsFinite(stop.Offset))
            {
                result.Errors.Add(new ValidationError("GRADIENT010", "Gradient stop offset is not a finite number", $"{path}.stops[{i}].offset"));
            }
            else if (stop.Offset is < 0 or > 1)
            {
                result.Errors.Add(new ValidationError("GRADIENT002", "Gradient stop offset must be between 0 and 1", $"{path}.stops[{i}].offset"));
            }

            if (!IsValidColor(stop.Color))
            {
                result.Errors.Add(new ValidationError("GRADIENT003", $"Invalid gradient stop color: {stop.Color}", $"{path}.stops[{i}].color"));
            }

            if (!double.IsFinite(stop.Opacity))
            {
                result.Errors.Add(new ValidationError("GRADIENT011", "Gradient stop opacity is not a finite number", $"{path}.stops[{i}].opacity"));
            }
            else if (stop.Opacity is < 0 or > 1)
            {
                result.Errors.Add(new ValidationError("GRADIENT004", "Gradient stop opacity must be between 0 and 1", $"{path}.stops[{i}].opacity"));
            }
        }

        switch (gradient)
        {
            case LinearGradient linear:
                ValidateFinite(linear.X1, $"{path}.x1", "GRADIENT006", "Linear gradient X1", result);
                ValidateFinite(linear.Y1, $"{path}.y1", "GRADIENT007", "Linear gradient Y1", result);
                ValidateFinite(linear.X2, $"{path}.x2", "GRADIENT008", "Linear gradient X2", result);
                ValidateFinite(linear.Y2, $"{path}.y2", "GRADIENT009", "Linear gradient Y2", result);
                break;
            case RadialGradient radial:
                ValidateFinite(radial.Cx, $"{path}.cx", "GRADIENT012", "Radial gradient Cx", result);
                ValidateFinite(radial.Cy, $"{path}.cy", "GRADIENT013", "Radial gradient Cy", result);
                if (!double.IsFinite(radial.R))
                {
                    result.Errors.Add(new ValidationError("GRADIENT014", "Radial gradient radius is not a finite number", $"{path}.r"));
                }
                else if (radial.R <= 0)
                {
                    result.Errors.Add(new ValidationError("GRADIENT005", "Radial gradient radius must be > 0", $"{path}.r"));
                }
                if (radial.Fx.HasValue)
                {
                    ValidateFinite(radial.Fx.Value, $"{path}.fx", "GRADIENT015", "Radial gradient Fx", result);
                }
                if (radial.Fy.HasValue)
                {
                    ValidateFinite(radial.Fy.Value, $"{path}.fy", "GRADIENT016", "Radial gradient Fy", result);
                }
                break;
        }
    }

    private static void ValidateFrameNode(
        Document document,
        FrameNode frame,
        string path,
        ValidationResult result,
        HashSet<Guid> seenIds,
        ValidationProfile profile,
        TokenDiagnosticBudget tokenDiagnostics)
    {
        if (!double.IsFinite(frame.Width))
        {
            result.Errors.Add(new ValidationError("FRAME003", "Frame width is not a finite number", $"{path}.width"));
        }
        else if (frame.Width < 0)
        {
            result.Errors.Add(new ValidationError("FRAME001", "Frame width cannot be negative", $"{path}.width"));
        }

        if (!double.IsFinite(frame.Height))
        {
            result.Errors.Add(new ValidationError("FRAME004", "Frame height is not a finite number", $"{path}.height"));
        }
        else if (frame.Height < 0)
        {
            result.Errors.Add(new ValidationError("FRAME002", "Frame height cannot be negative", $"{path}.height"));
        }

        ValidateChildren(document, frame.Children, path, result, seenIds, profile, tokenDiagnostics);
    }

    private static void ValidateGroupNode(
        Document document,
        GroupNode group,
        string path,
        ValidationResult result,
        HashSet<Guid> seenIds,
        ValidationProfile profile,
        TokenDiagnosticBudget tokenDiagnostics)
    {
        ValidateChildren(document, group.Children, path, result, seenIds, profile, tokenDiagnostics);
    }

    private static void ValidateChildren(
        Document document,
        List<Node> children,
        string path,
        ValidationResult result,
        HashSet<Guid> seenIds,
        ValidationProfile profile,
        TokenDiagnosticBudget tokenDiagnostics)
    {
        var orders = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            if (child is null)
            {
                result.Errors.Add(new ValidationError("NODE011", "Container has a null child", $"{path}.children[{i}]"));
                continue;
            }
            if (!orders.Add(child.Order))
            {
                result.Errors.Add(new ValidationError(
                    "NODE010",
                    $"Duplicate sibling order '{child.Order}'",
                    $"{path}.children[{i}].order"));
            }
            ValidateNode(document, child, $"{path}.children[{i}]", result, seenIds, profile, tokenDiagnostics);
        }
    }

    private static void ValidateRectangleNode(RectangleNode rect, string path, ValidationResult result)
    {
        if (!double.IsFinite(rect.Width))
        {
            result.Errors.Add(new ValidationError("RECT004", "Rectangle width is not a finite number", $"{path}.width"));
        }
        else if (rect.Width < 0)
        {
            result.Errors.Add(new ValidationError("RECT001", "Rectangle width cannot be negative", $"{path}.width"));
        }

        if (!double.IsFinite(rect.Height))
        {
            result.Errors.Add(new ValidationError("RECT005", "Rectangle height is not a finite number", $"{path}.height"));
        }
        else if (rect.Height < 0)
        {
            result.Errors.Add(new ValidationError("RECT002", "Rectangle height cannot be negative", $"{path}.height"));
        }

        if (!double.IsFinite(rect.CornerRadius))
        {
            result.Errors.Add(new ValidationError("RECT006", "Corner radius is not a finite number", $"{path}.cornerRadius"));
        }
        else if (rect.CornerRadius < 0)
        {
            result.Errors.Add(new ValidationError("RECT003", "Corner radius cannot be negative", $"{path}.cornerRadius"));
        }
    }

    private static void ValidateEllipseNode(EllipseNode ellipse, string path, ValidationResult result)
    {
        if (!double.IsFinite(ellipse.Width))
        {
            result.Errors.Add(new ValidationError("ELLIPSE003", "Ellipse width is not a finite number", $"{path}.width"));
        }
        else if (ellipse.Width < 0)
        {
            result.Errors.Add(new ValidationError("ELLIPSE001", "Ellipse width cannot be negative", $"{path}.width"));
        }

        if (!double.IsFinite(ellipse.Height))
        {
            result.Errors.Add(new ValidationError("ELLIPSE004", "Ellipse height is not a finite number", $"{path}.height"));
        }
        else if (ellipse.Height < 0)
        {
            result.Errors.Add(new ValidationError("ELLIPSE002", "Ellipse height cannot be negative", $"{path}.height"));
        }
    }

    private static void ValidatePathNode(PathNode pathNode, string path, ValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(pathNode.D))
        {
            result.Warnings.Add(new ValidationWarning("PATH001", "Path data is empty", $"{path}.d"));
        }
    }

    private static void ValidateTextNode(TextNode text, string path, ValidationResult result)
    {
        if (!double.IsFinite(text.FontSize))
        {
            result.Errors.Add(new ValidationError("TEXT003", "Font size is not a finite number", $"{path}.fontSize"));
        }
        else if (text.FontSize <= 0)
        {
            result.Errors.Add(new ValidationError("TEXT001", "Font size must be > 0", $"{path}.fontSize"));
        }

        if (text.FontWeight is < 100 or > 900)
        {
            result.Warnings.Add(new ValidationWarning("TEXT002", "Font weight should be between 100 and 900", $"{path}.fontWeight"));
        }

        if (text.MaxWidth is { } maxWidth && (!double.IsFinite(maxWidth) || maxWidth <= 0))
        {
            result.Errors.Add(new ValidationError("TEXT004", "Max width must be > 0", $"{path}.maxWidth"));
        }

        if (text.LineHeight is { } lineHeight && (!double.IsFinite(lineHeight) || lineHeight <= 0))
        {
            result.Errors.Add(new ValidationError("TEXT005", "Line height must be > 0", $"{path}.lineHeight"));
        }

        if (!TextLayout.IsValidTextAlign(text.TextAlign))
        {
            result.Errors.Add(new ValidationError("TEXT006",
                $"Text align must be one of {string.Join(", ", TextLayout.TextAlignValues)}", $"{path}.textAlign"));
        }

        if (!TextLayout.IsValidVerticalAlign(text.VerticalAlign))
        {
            result.Errors.Add(new ValidationError("TEXT006",
                $"Vertical align must be one of {string.Join(", ", TextLayout.VerticalAlignValues)}", $"{path}.verticalAlign"));
        }

        if (text.LetterSpacing is { } letterSpacing && !double.IsFinite(letterSpacing))
        {
            result.Errors.Add(new ValidationError("TEXT007", "Letter spacing is not a finite number", $"{path}.letterSpacing"));
        }
    }

    private static void ValidateLineNode(LineNode line, string path, ValidationResult result)
    {
        ValidateFinite(line.X1, $"{path}.x1", "LINE002", "Line X1", result);
        ValidateFinite(line.Y1, $"{path}.y1", "LINE003", "Line Y1", result);
        ValidateFinite(line.X2, $"{path}.x2", "LINE004", "Line X2", result);
        ValidateFinite(line.Y2, $"{path}.y2", "LINE005", "Line Y2", result);

        if (line.X1 == line.X2 && line.Y1 == line.Y2)
        {
            result.Warnings.Add(new ValidationWarning("LINE001", "Line has zero length (start and end points are identical)", path));
        }
    }

    private static void ValidatePolygonNode(PolygonNode polygon, string path, ValidationResult result)
    {
        if (polygon.Points.Count < 2)
        {
            result.Warnings.Add(new ValidationWarning("POLYGON001", "Polygon has fewer than 2 points", $"{path}.points"));
        }

        if (polygon.Closed && polygon.Points.Count < 3)
        {
            result.Warnings.Add(new ValidationWarning("POLYGON002", "Closed polygon has fewer than 3 points", $"{path}.points"));
        }
    }

    private static void ValidateImageNode(ImageNode image, string path, ValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(image.Src))
        {
            result.Warnings.Add(new ValidationWarning("IMAGE001", "Image source is empty", $"{path}.src"));
        }

        if (!double.IsFinite(image.Width))
        {
            result.Errors.Add(new ValidationError("IMAGE004", "Image width is not a finite number", $"{path}.width"));
        }
        else if (image.Width < 0)
        {
            result.Errors.Add(new ValidationError("IMAGE002", "Image width cannot be negative", $"{path}.width"));
        }

        if (!double.IsFinite(image.Height))
        {
            result.Errors.Add(new ValidationError("IMAGE005", "Image height is not a finite number", $"{path}.height"));
        }
        else if (image.Height < 0)
        {
            result.Errors.Add(new ValidationError("IMAGE003", "Image height cannot be negative", $"{path}.height"));
        }
    }

    private static bool IsValidColor(string color)
    {
        return DocumentLimits.IsValidColor(color);
    }

    private static void ValidateTokens(
        Document document,
        ValidationResult result,
        TokenDiagnosticBudget tokenDiagnostics)
    {
        if (document.Tokens?.Colors is { } colors)
        {
            if (colors.Count > DocumentLimits.MaxDesignTokensPerCategory)
            {
                result.Errors.Add(new ValidationError(
                    "RESOURCE002",
                    $"Color token count exceeds maximum ({DocumentLimits.MaxDesignTokensPerCategory})",
                    "tokens.colors"));
                return;
            }

            foreach (var (name, value) in colors)
            {
                var path = $"tokens.colors.{name}";
                if (!DocumentLimits.IsValidTokenName(name))
                    tokenDiagnostics.Add(new ValidationError("TOKEN001", "Invalid or oversized color token name", path));
                if (value is null)
                {
                    tokenDiagnostics.Add(new ValidationError("TOKEN002", "Color token value is null", path));
                    continue;
                }
                if (DocumentLimits.IsValidColor(value))
                    continue;

                var target = GetTokenTarget(value, colors);
                if (target is null)
                {
                    tokenDiagnostics.Add(new ValidationError("TOKEN002", "Color token must be a strict color or token reference", path));
                    continue;
                }

                if (!TryResolveColorToken(name, colors, new HashSet<string>(StringComparer.Ordinal)))
                    tokenDiagnostics.Add(new ValidationError("TOKEN003", "Color token reference is missing or cyclic", path));
            }
        }

        if (document.Tokens?.Typography is { } typography)
        {
            if (typography.Count > DocumentLimits.MaxDesignTokensPerCategory)
            {
                result.Errors.Add(new ValidationError(
                    "RESOURCE002",
                    $"Typography token count exceeds maximum ({DocumentLimits.MaxDesignTokensPerCategory})",
                    "tokens.typography"));
                return;
            }

            foreach (var (name, token) in typography)
            {
                var path = $"tokens.typography.{name}";
                if (!DocumentLimits.IsValidTokenName(name))
                    tokenDiagnostics.Add(new ValidationError("TOKEN004", "Invalid or oversized typography token name", path));
                if (token is null)
                {
                    tokenDiagnostics.Add(new ValidationError("TOKEN005", "Typography token is null", path));
                    continue;
                }
                if (token.FontFamily is not null && string.IsNullOrWhiteSpace(token.FontFamily))
                    tokenDiagnostics.Add(new ValidationError("TOKEN005", "Typography font family cannot be blank", $"{path}.fontFamily"));
                if (token.FontSize is { } size && (!double.IsFinite(size) || size <= 0))
                    tokenDiagnostics.Add(new ValidationError("TOKEN005", "Typography font size must be finite and positive", $"{path}.fontSize"));
                if (token.FontWeight is { } weight && weight is < 100 or > 900)
                    tokenDiagnostics.Add(new ValidationError("TOKEN005", "Typography font weight must be between 100 and 900", $"{path}.fontWeight"));
                if (token.LineHeight is { } lineHeight && (!double.IsFinite(lineHeight) || lineHeight < 0))
                    tokenDiagnostics.Add(new ValidationError("TOKEN005", "Typography line height must be finite and non-negative", $"{path}.lineHeight"));
            }
        }
    }

    private static void ValidateNodeTokenReferences(
        Document document,
        Node node,
        string path,
        TokenDiagnosticBudget tokenDiagnostics)
    {
        ValidateColorReference(document, node.Style?.Fill?.TokenRef, $"{path}.style.fill.tokenRef", tokenDiagnostics);
        ValidateColorReference(document, node.Style?.Stroke?.TokenRef, $"{path}.style.stroke.tokenRef", tokenDiagnostics);
        if (node is TextNode { TypographyTokenRef: { } typographyReference })
        {
            var name = NormalizeTokenReference(typographyReference);
            if (name is null || document.Tokens?.Typography?.ContainsKey(name) != true)
                tokenDiagnostics.Add(new ValidationError("TOKEN007", "Typography token reference was not found", $"{path}.typographyTokenRef"));
        }
    }

    private static void ValidateColorReference(
        Document document,
        string? reference,
        string path,
        TokenDiagnosticBudget tokenDiagnostics)
    {
        if (reference is null)
            return;
        var name = NormalizeTokenReference(reference);
        if (name is null || document.Tokens?.Colors?.ContainsKey(name) != true)
            tokenDiagnostics.Add(new ValidationError("TOKEN006", "Color token reference was not found", path));
    }

    private static string? NormalizeTokenReference(string reference)
    {
        if (DocumentLimits.TryGetTokenReference(reference, out var braced))
            return braced;
        return DocumentLimits.IsValidTokenName(reference) ? reference : null;
    }

    private static string? GetTokenTarget(string value, IReadOnlyDictionary<string, string> colors)
    {
        if (DocumentLimits.TryGetTokenReference(value, out var braced))
            return braced;
        return DocumentLimits.IsValidTokenName(value) && colors.ContainsKey(value) ? value : null;
    }

    private static bool TryResolveColorToken(
        string name,
        IReadOnlyDictionary<string, string> colors,
        HashSet<string> visited)
    {
        for (var depth = 0; depth < DocumentLimits.MaxTokenAliasDepth; depth++)
        {
            if (!visited.Add(name) || !colors.TryGetValue(name, out var value) || value is null)
                return false;
            if (DocumentLimits.IsValidColor(value))
                return true;

            var target = GetTokenTarget(value, colors);
            if (target is null)
                return false;
            name = target;
        }

        return false;
    }

    private static void ValidateFinite(double value, string path, string code, string name, ValidationResult result)
    {
        if (!double.IsFinite(value))
        {
            result.Errors.Add(new ValidationError(code, $"{name} is not a finite number", path));
        }
    }

    private sealed class TokenDiagnosticBudget(ValidationResult result)
    {
        private int _count;
        private bool _limitReported;

        public void Add(ValidationError error)
        {
            if (_count < DocumentLimits.MaxTokenValidationDiagnostics)
            {
                result.Errors.Add(error);
                _count++;
            }
            else if (!_limitReported)
            {
                result.Errors.Add(new ValidationError(
                    "RESOURCE003",
                    $"Token diagnostics exceed maximum ({DocumentLimits.MaxTokenValidationDiagnostics})",
                    "tokens"));
                _limitReported = true;
            }
        }
    }
}
