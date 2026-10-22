using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Layout;
using FTPlus.AgentGraphicDesign.Core.Ordering;

namespace FTPlus.AgentGraphicDesign.Core.Import;

/// <summary>
/// Bounded, best-effort SVG import. All mutable import state is scoped to one
/// call, so an instance can safely be shared by concurrent callers.
/// </summary>
public sealed partial class SvgImportService
{
    private static readonly XNamespace SvgNs = "http://www.w3.org/2000/svg";
    private static readonly XNamespace XlinkNs = "http://www.w3.org/1999/xlink";
    public const int MaxInputCharacters = 1_000_000;
    public const int MaxInputBytes = 4_000_000;
    public const int MaxXmlDepth = 128;
    public const int MaxElementCount = 10_000;
    public const int MaxImportDepth = 100;
    public const int MaxPathDataCharacters = 256_000;
    public const int MaxTextCharacters = 64_000;
    public const int MaxDataImageBytes = SvgResourcePolicy.MaxDecodedImageBytes;
    public const double MaxDimension = 1_000_000;
    public const int MaxGradientAliasDepth = 64;
    public const int MaxClonedGradientStops = 250_000;
    public const int MaxUseExpansionDepth = 20;
    public const int MaxUseExpansions = 2_000;
    public const double MaxTransformMagnitude = 1_000_000_000;
    private const int MaxTransformCharacters = 8_192;

    [GeneratedRegex(@"([a-zA-Z][a-zA-Z0-9]*)\s*\(([^)]*)\)", RegexOptions.CultureInvariant)]
    private static partial Regex TransformFunctionPattern();

    [GeneratedRegex(@"\G[+-]?(?:(?:\d+\.?\d*)|(?:\.\d+))(?:[eE][+-]?\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();

    public SvgImportResult Import(string svgContent, SvgImportOptions? options = null) =>
        Import(svgContent, options, CancellationToken.None);

    public SvgImportResult Import(string svgContent, CancellationToken cancellationToken) =>
        Import(svgContent, null, cancellationToken);

    public SvgImportResult Import(string svgContent, SvgImportOptions? options, CancellationToken cancellationToken)
    {
        options ??= new SvgImportOptions();
        var call = new ImportCallContext(cancellationToken);

        try
        {
            var xml = ParseBoundedXml(svgContent, call);
            var svg = xml.Root;
            if (svg is null || !IsSvgElement(svg, "svg"))
                return SvgImportResult.Fail("SVG_PARSE_ERROR", "Root element is not an SVG", call.Warnings);

            ValidateElementResources(svg, call);
            var viewport = ParseViewport(svg, call);
            ValidateViewport(viewport);

            var document = new Document
            {
                Schema = Document.SchemaUrl,
                Id = Guid.NewGuid(),
                Name = options.DocumentName ?? "imported",
                Version = 1,
                Revn = 1,
                Canvas = new Canvas
                {
                    Width = viewport.Width,
                    Height = viewport.Height,
                    Background = ParseBackground(svg)
                },
                Root = new FrameNode
                {
                    Id = Guid.NewGuid(),
                    Name = "root",
                    Width = viewport.Width,
                    Height = viewport.Height,
                    ClipContent = true,
                    Children = []
                }
            };

            ParseDefs(svg, call);
            BuildIdIndex(svg, call);
            IContainerNode importTarget = document.Root;
            if (viewport.MinX != 0 || viewport.MinY != 0)
            {
                var viewBoxGroup = new GroupNode
                {
                    Id = Guid.NewGuid(),
                    Name = "viewBox-content",
                    Order = FractionalIndex.Between(null, null),
                    Transform = CreateTranslation(-viewport.MinX, -viewport.MinY),
                    Children = []
                };
                document.Root.Children.Add(viewBoxGroup);
                importTarget = viewBoxGroup;
            }
            ImportChildren(svg, importTarget, 0, call);
            cancellationToken.ThrowIfCancellationRequested();

            return new SvgImportResult
            {
                Success = true,
                Document = document,
                Warnings = call.Warnings.ToList()
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SvgResourceLimitException ex)
        {
            return SvgImportResult.Fail("SVG_RESOURCE_LIMIT", ex.Message, call.Warnings);
        }
        catch (Exception ex)
        {
            return SvgImportResult.Fail(
                "SVG_PARSE_ERROR",
                $"Failed to parse SVG: {ErrorSanitizer.SanitizePaths(ex.Message)}",
                call.Warnings);
        }
    }

    private static XDocument ParseBoundedXml(string svgContent, ImportCallContext call)
    {
        if (svgContent is null)
            throw new XmlException("SVG content is null");
        if (svgContent.Length > MaxInputCharacters)
            throw new SvgResourceLimitException($"SVG input exceeds maximum length ({MaxInputCharacters} characters)");
        if (Encoding.UTF8.GetByteCount(svgContent) > MaxInputBytes)
            throw new SvgResourceLimitException($"SVG input exceeds maximum UTF-8 size ({MaxInputBytes} bytes)");

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxInputCharacters,
            MaxCharactersFromEntities = 0,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            CheckCharacters = true
        };

        // Scan once without allocation to enforce depth/count and cancellation
        // before constructing the LINQ-to-XML tree.
        using (var scanReader = XmlReader.Create(new StringReader(svgContent), settings))
        {
            var elementCount = 0;
            while (scanReader.Read())
            {
                if ((elementCount & 127) == 0)
                    call.CancellationToken.ThrowIfCancellationRequested();
                if (scanReader.NodeType != XmlNodeType.Element)
                    continue;
                if (++elementCount > MaxElementCount)
                    throw new SvgResourceLimitException($"SVG element count exceeds maximum ({MaxElementCount})");
                if (scanReader.Depth > MaxXmlDepth)
                    throw new SvgResourceLimitException($"SVG XML depth exceeds maximum ({MaxXmlDepth})");
            }
        }

        call.CancellationToken.ThrowIfCancellationRequested();
        using var loadReader = XmlReader.Create(new StringReader(svgContent), settings);
        var document = XDocument.Load(loadReader, LoadOptions.None);
        call.CancellationToken.ThrowIfCancellationRequested();
        return document;
    }

    private static void ValidateElementResources(XElement root, ImportCallContext call)
    {
        var index = 0;
        foreach (var element in root.DescendantsAndSelf())
        {
            if ((index++ & 127) == 0)
                call.CancellationToken.ThrowIfCancellationRequested();

            if (element.Name.LocalName == "path" && (element.Attribute("d")?.Value.Length ?? 0) > MaxPathDataCharacters)
                throw new SvgResourceLimitException($"SVG path data exceeds maximum ({MaxPathDataCharacters} characters)");
            if (element.Name.LocalName == "text" && element.Value.Length > MaxTextCharacters)
                throw new SvgResourceLimitException($"SVG text content exceeds maximum ({MaxTextCharacters} characters)");
            if ((element.Attribute("transform")?.Value.Length ?? 0) > MaxTransformCharacters)
                throw new SvgResourceLimitException($"SVG transform exceeds maximum ({MaxTransformCharacters} characters)");
        }
    }

    private static void ParseDefs(XElement svg, ImportCallContext call)
    {
        foreach (var defs in svg.DescendantsAndSelf().Where(element => element.Name.LocalName == "defs"))
        {
            foreach (var element in defs.Elements())
            {
                var id = element.Attribute("id")?.Value;
                var localName = element.Name.LocalName;
                if (localName is "linearGradient" or "radialGradient")
                {
                    if (!string.IsNullOrEmpty(id))
                    {
                        if (!call.GradientElements.TryAdd(id, element))
                            call.AddWarning(ImportWarningCode.ParseError, $"Duplicate gradient id '{id}' ignored", id);
                    }
                }
                else if (localName is "filter" or "mask" or "pattern" or "marker")
                {
                    call.AddWarning(ImportWarningCode.UnsupportedElement, $"SVG <{localName}> definitions are not supported", localName);
                }
                // <symbol> and <clipPath> are captured by BuildIdIndex and instantiated on demand
                // through <use> / clip-path (D-11); a bare definition renders nothing.
            }
        }
    }

    /// <summary>
    /// Indexes every element carrying an <c>id</c> so that <c>&lt;use href="#id"&gt;</c> and
    /// <c>clip-path="url(#id)"</c> can resolve local references (D-11). Ids are unique per SVG;
    /// on a duplicate the first definition wins.
    /// </summary>
    private static void BuildIdIndex(XElement svg, ImportCallContext call)
    {
        var index = 0;
        foreach (var element in svg.DescendantsAndSelf())
        {
            if ((index++ & 127) == 0)
                call.CancellationToken.ThrowIfCancellationRequested();
            var id = element.Attribute("id")?.Value;
            if (!string.IsNullOrEmpty(id))
                call.ElementsById.TryAdd(id, element);
        }

        foreach (var id in call.GradientElements.Keys.OrderBy(value => value, StringComparer.Ordinal))
        {
            call.CancellationToken.ThrowIfCancellationRequested();
            ResolveGradient(id, call, new HashSet<string>(StringComparer.Ordinal), 0);
        }
    }

    private static Gradient? ResolveGradient(
        string id,
        ImportCallContext call,
        HashSet<string> resolving,
        int depth)
    {
        call.CancellationToken.ThrowIfCancellationRequested();
        if (depth >= MaxGradientAliasDepth)
            throw new SvgResourceLimitException($"SVG gradient alias depth exceeds maximum ({MaxGradientAliasDepth})");
        if (call.Gradients.TryGetValue(id, out var existing))
            return existing;
        if (!call.GradientElements.TryGetValue(id, out var element))
            return null;
        if (!resolving.Add(id))
        {
            call.AddWarning(ImportWarningCode.MissingReference, $"Gradient reference cycle detected at '{id}'", id);
            return null;
        }

        Gradient? parent = null;
        var href = element.Attribute(XlinkNs + "href")?.Value ?? element.Attribute("href")?.Value;
        if (href?.StartsWith('#') == true)
            parent = ResolveGradient(href[1..], call, resolving, depth + 1);

        Gradient gradient = element.Name.LocalName == "linearGradient"
            ? ParseLinearGradient(element, parent as LinearGradient, call)
            : ParseRadialGradient(element, parent as RadialGradient, call);
        call.Gradients[id] = gradient;
        resolving.Remove(id);
        return gradient;
    }

    private static LinearGradient ParseLinearGradient(XElement element, LinearGradient? parent, ImportCallContext call) =>
        new()
        {
            X1 = ParsePercentOrNumber(element.Attribute("x1")?.Value, parent?.X1 ?? 0),
            Y1 = ParsePercentOrNumber(element.Attribute("y1")?.Value, parent?.Y1 ?? 0),
            X2 = ParsePercentOrNumber(element.Attribute("x2")?.Value, parent?.X2 ?? 1),
            Y2 = ParsePercentOrNumber(element.Attribute("y2")?.Value, parent?.Y2 ?? 0),
            Stops = ParseGradientStops(element, call) is { Count: > 0 } stops
                ? stops
                : parent is null ? [] : CloneStops(parent.Stops, 1, call)
        };

    private static RadialGradient ParseRadialGradient(XElement element, RadialGradient? parent, ImportCallContext call) =>
        new()
        {
            Cx = ParsePercentOrNumber(element.Attribute("cx")?.Value, parent?.Cx ?? 0.5),
            Cy = ParsePercentOrNumber(element.Attribute("cy")?.Value, parent?.Cy ?? 0.5),
            R = ParsePercentOrNumber(element.Attribute("r")?.Value, parent?.R ?? 0.5),
            Fx = element.Attribute("fx") is { } fx ? ParsePercentOrNumber(fx.Value, parent?.Fx ?? 0.5) : parent?.Fx,
            Fy = element.Attribute("fy") is { } fy ? ParsePercentOrNumber(fy.Value, parent?.Fy ?? 0.5) : parent?.Fy,
            Stops = ParseGradientStops(element, call) is { Count: > 0 } stops
                ? stops
                : parent is null ? [] : CloneStops(parent.Stops, 1, call)
        };

    private static List<GradientStop> ParseGradientStops(XElement gradientElement, ImportCallContext call)
    {
        var stops = new List<GradientStop>();
        foreach (var stop in gradientElement.Elements().Where(element => element.Name.LocalName == "stop"))
        {
            call.CancellationToken.ThrowIfCancellationRequested();
            var offset = Math.Clamp(ParsePercentOrNumber(GetLocalProperty(stop, "offset"), 0), 0, 1);
            var color = NormalizeColor(GetLocalProperty(stop, "stop-color") ?? "#000000");
            var opacity = Math.Clamp(ParseDouble(GetLocalProperty(stop, "stop-opacity"), 1), 0, 1);
            color = ApplyColorOpacity(color, 1, call, "stop-opacity");
            stops.Add(new GradientStop { Offset = offset, Color = color, Opacity = opacity });
        }
        return stops;
    }

    private static void ImportChildren(XElement parent, IContainerNode container, int depth, ImportCallContext call)
    {
        if (depth >= MaxImportDepth)
        {
            call.AddWarning(
                ImportWarningCode.UnsupportedFeature,
                $"SVG nesting depth exceeds maximum ({MaxImportDepth}); children skipped",
                "max-depth");
            return;
        }

        string? lastOrder = null;
        var index = 0;
        foreach (var element in parent.Elements())
        {
            if ((index++ & 63) == 0)
                call.CancellationToken.ThrowIfCancellationRequested();
            var node = ImportElement(element, depth + 1, call);
            if (node is null)
                continue;
            node.Order = FractionalIndex.Between(lastOrder, null);
            lastOrder = node.Order;
            container.Children.Add(node);
        }
    }

    private static Node? ImportElement(XElement element, int depth, ImportCallContext call)
    {
        var localName = element.Name.LocalName;
        // Definitions render nothing on their own; they are instantiated via <use>/clip-path.
        if (localName is "defs" or "title" or "desc" or "metadata" or "symbol" or "clipPath")
            return null;
        if (localName == "style")
        {
            call.AddWarning(ImportWarningCode.UnsupportedFeature, "SVG stylesheet rules are not supported; use presentation attributes or inline style", "style");
            return null;
        }

        WarnForUnsupportedAttributes(element, call);
        Node? node = localName switch
        {
            "rect" => ImportRect(element, call),
            "circle" => ImportCircle(element, call),
            "ellipse" => ImportEllipse(element, call),
            "line" => ImportLine(element, call),
            "polyline" => ImportPolyline(element, false, call),
            "polygon" => ImportPolyline(element, true, call),
            "path" => ImportPath(element, call),
            "text" => ImportText(element, call),
            "g" => ImportGroup(element, depth, call),
            "svg" => ImportNestedSvg(element, depth, call),
            "image" => ImportImage(element, call),
            "use" => ImportUse(element, depth, call),
            _ => null
        };

        if (node is null && localName != "use")
            call.AddWarning(ImportWarningCode.UnsupportedElement, $"SVG <{localName}> is not supported", localName);
        if (node is not null)
        {
            ValidateImportedNode(node);
            node = ApplyClipPath(element, node, call);
        }
        return node;
    }

    private static void ValidateImportedNode(Node node)
    {
        switch (node)
        {
            case FrameNode frame:
                ValidateNonNegativeGeometry(frame.Width, "frame width");
                ValidateNonNegativeGeometry(frame.Height, "frame height");
                break;
            case RectangleNode rectangle:
                ValidateNonNegativeGeometry(rectangle.Width, "rectangle width");
                ValidateNonNegativeGeometry(rectangle.Height, "rectangle height");
                ValidateNonNegativeGeometry(rectangle.CornerRadius, "rectangle corner radius");
                break;
            case EllipseNode ellipse:
                ValidateNonNegativeGeometry(ellipse.Width, "ellipse width");
                ValidateNonNegativeGeometry(ellipse.Height, "ellipse height");
                break;
            case LineNode line:
                ValidateGeometry(line.X1, "line X1");
                ValidateGeometry(line.Y1, "line Y1");
                ValidateGeometry(line.X2, "line X2");
                ValidateGeometry(line.Y2, "line Y2");
                break;
            case PolygonNode polygon:
                foreach (var point in polygon.Points)
                {
                    ValidateGeometry(point.X, "polygon X");
                    ValidateGeometry(point.Y, "polygon Y");
                }
                break;
            case ImageNode image:
                ValidateNonNegativeGeometry(image.Width, "image width");
                ValidateNonNegativeGeometry(image.Height, "image height");
                break;
            case TextNode text when !double.IsFinite(text.FontSize) || text.FontSize <= 0 || text.FontSize > 4096:
                throw new SvgResourceLimitException("SVG font size must be finite and in the range (0, 4096]");
        }

        if (node.Style?.Stroke is { } stroke)
        {
            ValidateNonNegativeGeometry(stroke.Width, "stroke width");
            ValidateNonNegativeGeometry(stroke.MiterLimit, "stroke miter limit");
            ValidateGeometry(stroke.DashOffset, "stroke dash offset");
        }
    }

    private static void ValidateNonNegativeGeometry(double value, string name)
    {
        ValidateGeometry(value, name);
        if (value < 0)
            throw new SvgResourceLimitException($"SVG {name} must not be negative");
    }

    private static void ValidateGeometry(double value, string name)
    {
        if (!double.IsFinite(value) || Math.Abs(value) > MaxDimension)
        {
            throw new SvgResourceLimitException(
                $"SVG {name} is non-finite or exceeds coordinate limit ({MaxDimension.ToString(CultureInfo.InvariantCulture)})");
        }
    }

    private static RectangleNode ImportRect(XElement element, ImportCallContext call)
    {
        var x = ParseDouble(element.Attribute("x")?.Value, 0);
        var y = ParseDouble(element.Attribute("y")?.Value, 0);
        var rx = ParseDouble(element.Attribute("rx")?.Value, 0);
        var ry = ParseDouble(element.Attribute("ry")?.Value, rx);
        return new RectangleNode
        {
            Id = Guid.NewGuid(),
            Name = element.Attribute("id")?.Value,
            Width = ParseDouble(element.Attribute("width")?.Value, 0),
            Height = ParseDouble(element.Attribute("height")?.Value, 0),
            CornerRadius = Math.Max(rx, ry),
            Transform = CreateTransform(x, y, element, call),
            Style = ParseStyle(element, call),
            Visible = ParseVisibility(element)
        };
    }

    private static EllipseNode ImportCircle(XElement element, ImportCallContext call)
    {
        var cx = ParseDouble(element.Attribute("cx")?.Value, 0);
        var cy = ParseDouble(element.Attribute("cy")?.Value, 0);
        var radius = ParseDouble(element.Attribute("r")?.Value, 0);
        return new EllipseNode
        {
            Id = Guid.NewGuid(),
            Name = element.Attribute("id")?.Value,
            Width = radius * 2,
            Height = radius * 2,
            Transform = CreateTransform(cx - radius, cy - radius, element, call),
            Style = ParseStyle(element, call),
            Visible = ParseVisibility(element)
        };
    }

    private static EllipseNode ImportEllipse(XElement element, ImportCallContext call)
    {
        var cx = ParseDouble(element.Attribute("cx")?.Value, 0);
        var cy = ParseDouble(element.Attribute("cy")?.Value, 0);
        var rx = ParseDouble(element.Attribute("rx")?.Value, 0);
        var ry = ParseDouble(element.Attribute("ry")?.Value, 0);
        return new EllipseNode
        {
            Id = Guid.NewGuid(),
            Name = element.Attribute("id")?.Value,
            Width = rx * 2,
            Height = ry * 2,
            Transform = CreateTransform(cx - rx, cy - ry, element, call),
            Style = ParseStyle(element, call),
            Visible = ParseVisibility(element)
        };
    }

    private static LineNode ImportLine(XElement element, ImportCallContext call) => new()
    {
        Id = Guid.NewGuid(),
        Name = element.Attribute("id")?.Value,
        X1 = ParseDouble(element.Attribute("x1")?.Value, 0),
        Y1 = ParseDouble(element.Attribute("y1")?.Value, 0),
        X2 = ParseDouble(element.Attribute("x2")?.Value, 0),
        Y2 = ParseDouble(element.Attribute("y2")?.Value, 0),
        Transform = ParseTransformAttribute(element, call),
        Style = ParseStyle(element, call),
        Visible = ParseVisibility(element)
    };

    private static PolygonNode ImportPolyline(XElement element, bool closed, ImportCallContext call) => new()
    {
        Id = Guid.NewGuid(),
        Name = element.Attribute("id")?.Value,
        Points = ParsePoints(element.Attribute("points")?.Value ?? string.Empty),
        Closed = closed,
        Transform = ParseTransformAttribute(element, call),
        Style = ParseStyle(element, call),
        Visible = ParseVisibility(element)
    };

    private static PathNode ImportPath(XElement element, ImportCallContext call) => new()
    {
        Id = Guid.NewGuid(),
        Name = element.Attribute("id")?.Value,
        D = element.Attribute("d")?.Value ?? string.Empty,
        Transform = ParseTransformAttribute(element, call),
        Style = ParseStyle(element, call),
        Visible = ParseVisibility(element)
    };

    private static TextNode ImportText(XElement element, ImportCallContext call)
    {
        var fontFamily = GetCascadedProperty(element, "font-family")?.Trim('\'', '"');
        var fontSizeValue = GetCascadedProperty(element, "font-size");
        var fontSize = fontSizeValue is null ? 16 : ParseFontSize(fontSizeValue);
        var y = ParseDouble(element.Attribute("y")?.Value, 0);
        var content = GetTextContent(element, y, fontSize, call);

        return new TextNode
        {
            Id = Guid.NewGuid(),
            Name = element.Attribute("id")?.Value,
            Text = content.Text,
            FontFamily = fontFamily,
            FontSize = fontSize,
            FontWeight = ParseFontWeight(GetCascadedProperty(element, "font-weight")),
            TextAlign = ParseTextAnchor(element, call),
            LetterSpacing = ParseLetterSpacing(element, fontSize, call),
            LineHeight = content.LineHeight,
            Transform = CreateTransform(
                ParseDouble(element.Attribute("x")?.Value, 0),
                y - fontSize,
                element,
                call),
            Style = ParseStyle(element, call),
            Visible = ParseVisibility(element)
        };
    }

    private static string? ParseTextAnchor(XElement element, ImportCallContext call)
    {
        var anchor = GetCascadedProperty(element, "text-anchor");
        if (anchor is null)
            return null;
        var mapped = TextLayout.FromSvgTextAnchor(anchor);
        if (mapped is null && !anchor.Trim().Equals("start", StringComparison.OrdinalIgnoreCase))
            call.AddWarning(ImportWarningCode.UnsupportedFeature, $"SVG text-anchor '{anchor}' is not modeled", "text-anchor");
        return mapped;
    }

    private static double? ParseLetterSpacing(XElement element, double fontSize, ImportCallContext call)
    {
        var value = GetCascadedProperty(element, "letter-spacing")?.Trim();
        if (value is null || value.Equals("normal", StringComparison.OrdinalIgnoreCase))
            return null;
        if (TryParseLength(value, fontSize, out var spacing))
            return spacing == 0 ? null : spacing;
        call.AddWarning(ImportWarningCode.UnsupportedFeature, $"SVG letter-spacing '{value}' is not modeled", "letter-spacing");
        return null;
    }

    /// <summary>Parses a unitless, <c>px</c>, or <c>em</c> length; other units are rejected.</summary>
    private static bool TryParseLength(string value, double fontSize, out double result)
    {
        result = 0;
        var text = value.Trim();
        var multiplier = 1d;
        if (text.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            text = text[..^2];
        }
        else if (text.EndsWith("em", StringComparison.OrdinalIgnoreCase))
        {
            text = text[..^2];
            multiplier = fontSize;
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || !double.IsFinite(parsed))
            return false;
        result = parsed * multiplier;
        return double.IsFinite(result);
    }

    /// <summary>
    /// Flattens a <c>&lt;text&gt;</c> element's content. A <c>&lt;tspan&gt;</c> that moves down
    /// (a non-zero <c>dy</c>, or a <c>y</c> below the previous line) starts a new line; other
    /// tspans continue the current line. When every line break uses the same pitch, that
    /// pitch becomes the node's <c>lineHeight</c>. Per-tspan styling and other positioning
    /// attributes are not modeled and produce a warning.
    /// </summary>
    private static TextContent GetTextContent(XElement element, double startY, double fontSize, ImportCallContext call)
    {
        var lines = new List<string>();
        var current = new StringBuilder();
        var currentY = startY;
        double? pitch = null;
        var pitchConsistent = true;
        var sawBreak = false;

        foreach (var node in element.Nodes())
        {
            if (node is XText textNode)
            {
                AppendSegment(current, textNode.Value.Trim());
                continue;
            }

            if (node is not XElement child || child.Name.LocalName != "tspan")
                continue;

            WarnForUnsupportedTspanAttributes(child, call);
            var lineShift = GetTspanLineShift(child, currentY, fontSize, call, out var shiftUnknown);
            if (lineShift is { } shift && shift > 0)
            {
                if (current.Length > 0 || lines.Count > 0)
                {
                    lines.Add(current.ToString());
                    current.Clear();
                    if (pitch is { } known && Math.Abs(known - shift) > 1e-6)
                        pitchConsistent = false;
                    pitch ??= shift;
                    sawBreak = true;
                }
                else
                {
                    call.AddWarning(ImportWarningCode.UnsupportedFeature, "Leading SVG <tspan> vertical offset is not modeled", "tspan-offset");
                }
                currentY += shift;
            }
            else if (shiftUnknown)
            {
                pitchConsistent = false;
            }

            AppendSegment(current, GetFlattenedText(child));
        }

        lines.Add(current.ToString());
        var lineHeight = sawBreak && pitchConsistent && pitch is { } value && fontSize > 0
            ? Math.Round(value / fontSize, 4)
            : (double?)null;
        return new TextContent(string.Join("\n", lines), lineHeight);
    }

    private static double? GetTspanLineShift(XElement tspan, double currentY, double fontSize, ImportCallContext call, out bool unknown)
    {
        unknown = false;
        if (tspan.Attribute("dy")?.Value is { } dy)
        {
            if (TryParseLength(dy, fontSize, out var shift))
                return shift;
            call.AddWarning(ImportWarningCode.UnsupportedFeature, $"SVG <tspan> dy '{dy}' is not modeled", "tspan-dy");
            unknown = true;
            return null;
        }

        if (tspan.Attribute("y")?.Value is { } y)
        {
            if (TryParseLength(y, fontSize, out var absolute))
                return absolute - currentY;
            call.AddWarning(ImportWarningCode.UnsupportedFeature, $"SVG <tspan> y '{y}' is not modeled", "tspan-y");
            unknown = true;
        }

        return null;
    }

    private static void WarnForUnsupportedTspanAttributes(XElement tspan, ImportCallContext call)
    {
        var unsupported = tspan.Attributes()
            .Where(attribute => attribute.Name.Namespace != XNamespace.Xmlns
                && attribute.Name.LocalName is not ("x" or "y" or "dy" or "id"))
            .Select(attribute => attribute.Name.LocalName)
            .ToList();
        if (unsupported.Count > 0)
        {
            call.AddWarning(ImportWarningCode.UnsupportedFeature,
                $"SVG <tspan> attributes are not modeled and were dropped: {string.Join(", ", unsupported)}", "tspan");
        }
        if (tspan.Elements().Any(child => child.Name.LocalName == "tspan"))
            call.AddWarning(ImportWarningCode.UnsupportedFeature, "Nested SVG <tspan> elements are flattened", "tspan");
    }

    private static string GetFlattenedText(XElement element)
    {
        var builder = new StringBuilder();
        foreach (var node in element.Nodes())
        {
            if (node is XText textNode)
                AppendSegment(builder, textNode.Value.Trim());
            else if (node is XElement child && child.Name.LocalName == "tspan")
                AppendSegment(builder, GetFlattenedText(child));
        }
        return builder.ToString();
    }

    private static void AppendSegment(StringBuilder builder, string segment)
    {
        if (segment.Length == 0)
            return;
        if (builder.Length > 0)
            builder.Append(' ');
        builder.Append(segment);
    }

    private readonly record struct TextContent(string Text, double? LineHeight);

    private static GroupNode ImportGroup(XElement element, int depth, ImportCallContext call)
    {
        var group = new GroupNode
        {
            Id = Guid.NewGuid(),
            Name = element.Attribute("id")?.Value,
            Transform = ParseTransformAttribute(element, call),
            Style = ParseStyle(element, call),
            Visible = ParseVisibility(element)
        };
        ImportChildren(element, group, depth, call);
        return group;
    }

    private static FrameNode ImportNestedSvg(XElement element, int depth, ImportCallContext call)
    {
        var viewport = ParseViewport(element, call);
        ValidateViewport(viewport);
        var frame = new FrameNode
        {
            Id = Guid.NewGuid(),
            Name = element.Attribute("id")?.Value,
            Width = viewport.Width,
            Height = viewport.Height,
            ClipContent = true,
            Transform = CreateTransform(
                ParseDouble(element.Attribute("x")?.Value, 0),
                ParseDouble(element.Attribute("y")?.Value, 0),
                element,
                call),
            Style = ParseStyle(element, call),
            Visible = ParseVisibility(element)
        };
        IContainerNode importTarget = frame;
        if (viewport.MinX != 0 || viewport.MinY != 0)
        {
            var viewBoxGroup = new GroupNode
            {
                Id = Guid.NewGuid(),
                Name = "viewBox-content",
                Order = FractionalIndex.Between(null, null),
                Transform = CreateTranslation(-viewport.MinX, -viewport.MinY),
                Children = []
            };
            frame.Children.Add(viewBoxGroup);
            importTarget = viewBoxGroup;
        }
        ImportChildren(element, importTarget, depth, call);
        return frame;
    }

    private static ImageNode ImportImage(XElement element, ImportCallContext call)
    {
        var href = element.Attribute(XlinkNs + "href")?.Value ?? element.Attribute("href")?.Value ?? string.Empty;
        return new ImageNode
        {
            Id = Guid.NewGuid(),
            Name = element.Attribute("id")?.Value,
            Src = SanitizeImageHref(href, call),
            Width = ParseDouble(element.Attribute("width")?.Value, 100),
            Height = ParseDouble(element.Attribute("height")?.Value, 100),
            Transform = CreateTransform(
                ParseDouble(element.Attribute("x")?.Value, 0),
                ParseDouble(element.Attribute("y")?.Value, 0),
                element,
                call),
            Style = ParseStyle(element, call),
            Visible = ParseVisibility(element)
        };
    }

    /// <summary>
    /// Expands a <c>&lt;use href="#id"&gt;</c> by instantiating the referenced local element
    /// (D-11). A referenced <c>&lt;symbol&gt;</c>/<c>&lt;svg&gt;</c> becomes a clipped frame
    /// viewport; any other element is imported and wrapped in a group carrying the use's own
    /// transform and its <c>x</c>/<c>y</c> offset. Reference cycles, nesting depth and total
    /// expansion count are bounded.
    /// </summary>
    private static Node? ImportUse(XElement element, int depth, ImportCallContext call)
    {
        var href = element.Attribute(XlinkNs + "href")?.Value ?? element.Attribute("href")?.Value;
        if (href is null || !href.StartsWith('#'))
        {
            call.AddWarning(ImportWarningCode.UnsupportedFeature, "SVG <use> without a local '#id' reference is not supported", "use");
            return null;
        }

        var id = href[1..];
        if (!call.ElementsById.TryGetValue(id, out var referenced))
        {
            call.AddWarning(ImportWarningCode.MissingReference, $"SVG <use> references unknown id '{id}'", "use");
            return null;
        }
        if (!call.ExpandingUse.Add(id))
        {
            call.AddWarning(ImportWarningCode.MissingReference, $"SVG <use> reference cycle detected at '{id}'", "use");
            return null;
        }

        try
        {
            call.ChargeUseExpansion();
            if (depth >= MaxUseExpansionDepth)
            {
                call.AddWarning(ImportWarningCode.UnsupportedFeature, $"SVG <use> nesting exceeds maximum ({MaxUseExpansionDepth}); reference skipped", "use");
                return null;
            }

            Node? instance = referenced.Name.LocalName switch
            {
                "symbol" or "svg" => ImportSymbolInstance(referenced, element, depth, call),
                _ => ImportElement(referenced, depth + 1, call)
            };
            if (instance is null)
                return null;

            var x = ParseDouble(element.Attribute("x")?.Value, 0);
            var y = ParseDouble(element.Attribute("y")?.Value, 0);
            return new GroupNode
            {
                Id = Guid.NewGuid(),
                Name = element.Attribute("id")?.Value ?? $"use-{id}",
                Transform = CreateTransform(x, y, element, call),
                Visible = ParseVisibility(element),
                Children = [instance]
            };
        }
        finally
        {
            call.ExpandingUse.Remove(id);
        }
    }

    /// <summary>
    /// Instantiates a <c>&lt;use&gt;</c> reference to a <c>&lt;symbol&gt;</c> or <c>&lt;svg&gt;</c>
    /// as a clipped frame viewport, mirroring nested-svg import. The instance size comes from the
    /// <c>&lt;use&gt;</c>'s width/height when present, otherwise the referenced element's own.
    /// </summary>
    private static FrameNode ImportSymbolInstance(XElement referenced, XElement use, int depth, ImportCallContext call)
    {
        var viewport = ParseViewport(referenced, call);
        var width = ParseDimension(use.Attribute("width")?.Value, viewport.Width);
        var height = ParseDimension(use.Attribute("height")?.Value, viewport.Height);
        ValidateViewport(new SvgViewport(viewport.MinX, viewport.MinY, width, height));

        var frame = new FrameNode
        {
            Id = Guid.NewGuid(),
            Name = referenced.Attribute("id")?.Value,
            Width = width,
            Height = height,
            ClipContent = true,
            Style = ParseStyle(referenced, call),
            Visible = ParseVisibility(referenced)
        };
        IContainerNode importTarget = frame;
        if (viewport.MinX != 0 || viewport.MinY != 0)
        {
            var viewBoxGroup = new GroupNode
            {
                Id = Guid.NewGuid(),
                Name = "viewBox-content",
                Order = FractionalIndex.Between(null, null),
                Transform = CreateTranslation(-viewport.MinX, -viewport.MinY),
                Children = []
            };
            frame.Children.Add(viewBoxGroup);
            importTarget = viewBoxGroup;
        }
        ImportChildren(referenced, importTarget, depth, call);
        return frame;
    }

    /// <summary>
    /// Maps a rectangular <c>clip-path="url(#id)"</c> to a clipping <see cref="FrameNode"/>
    /// (D-11). Only a clipPath whose sole content is an axis-aligned, untransformed,
    /// non-rounded <c>&lt;rect&gt;</c> in <c>userSpaceOnUse</c> is modeled; any other clip-path
    /// keeps its unsupported-feature warning and the node is returned unclipped.
    /// </summary>
    private static Node ApplyClipPath(XElement element, Node node, ImportCallContext call)
    {
        var clip = GetLocalProperty(element, "clip-path");
        if (clip is null || clip.Equals("none", StringComparison.OrdinalIgnoreCase))
            return node;
        if (TryResolveRectClip(clip, call) is not { } rect)
        {
            call.AddWarning(ImportWarningCode.UnsupportedFeature, "SVG clip-path is not supported", "clip-path");
            return node;
        }

        // The clip rect is in the same user space as the node. Model it as a clipping frame at the
        // rect's position and shift the node back into the frame's local space so geometry is kept.
        var content = new GroupNode
        {
            Id = Guid.NewGuid(),
            Name = "clip-content",
            Transform = CreateTranslation(-rect.X, -rect.Y),
            Children = [node]
        };
        return new FrameNode
        {
            Id = Guid.NewGuid(),
            Name = node.Name is null ? "clip" : $"clip-{node.Name}",
            Width = rect.Width,
            Height = rect.Height,
            ClipContent = true,
            Transform = CreateTranslation(rect.X, rect.Y),
            Children = [content]
        };
    }

    private static RectClip? TryResolveRectClip(string clipValue, ImportCallContext call)
    {
        var id = ExtractUrlReference(clipValue);
        if (id is null
            || !call.ElementsById.TryGetValue(id, out var clipPath)
            || clipPath.Name.LocalName != "clipPath")
            return null;

        // Only default user-space units and an untransformed clipPath are modeled.
        var units = clipPath.Attribute("clipPathUnits")?.Value;
        if (units is not null && !units.Equals("userSpaceOnUse", StringComparison.Ordinal))
            return null;
        if (clipPath.Attribute("transform") is not null)
            return null;

        var shapes = clipPath.Elements()
            .Where(e => e.Name.LocalName is not ("title" or "desc" or "metadata"))
            .ToList();
        if (shapes.Count != 1 || shapes[0].Name.LocalName != "rect")
            return null;

        var rect = shapes[0];
        if (rect.Attribute("transform") is not null)
            return null;
        if (ParseDouble(rect.Attribute("rx")?.Value, 0) != 0 || ParseDouble(rect.Attribute("ry")?.Value, 0) != 0)
            return null;

        var width = ParseDouble(rect.Attribute("width")?.Value, 0);
        var height = ParseDouble(rect.Attribute("height")?.Value, 0);
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0
            || width > MaxDimension || height > MaxDimension)
            return null;

        var x = ParseDouble(rect.Attribute("x")?.Value, 0);
        var y = ParseDouble(rect.Attribute("y")?.Value, 0);
        if (!double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) > MaxDimension || Math.Abs(y) > MaxDimension)
            return null;

        return new RectClip(x, y, width, height);
    }

    private static string? ExtractUrlReference(string value)
    {
        var trimmed = value.Trim();
        if (!trimmed.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
            return null;
        var close = trimmed.IndexOf(')');
        if (close < 0)
            return null;
        var inner = trimmed[4..close].Trim().Trim('"', '\'');
        return inner.StartsWith('#') ? inner[1..] : null;
    }

    private readonly record struct RectClip(double X, double Y, double Width, double Height);

    private static string SanitizeImageHref(string href, ImportCallContext call)
    {
        return SvgResourcePolicy.TryGetSafeImageHref(
            href,
            out var safeHref,
            out var reason,
            call.CancellationToken)
            ? safeHref
            : RejectImageHref(call, $"{reason} stripped");
    }

    private static string RejectImageHref(ImportCallContext call, string message)
    {
        call.AddWarning(ImportWarningCode.UnsupportedFeature, message, "image-href-stripped");
        return string.Empty;
    }

    private static Style ParseStyle(XElement element, ImportCallContext call)
    {
        var opacity = Math.Clamp(ParseDouble(GetLocalProperty(element, "opacity"), 1), 0, 1);
        return new Style
        {
            Fill = ParseFill(element, call),
            Stroke = ParseStroke(element, call),
            Opacity = opacity,
            BlendMode = ParseBlendMode(element),
            FillRule = ParseFillRule(element, call)
        };
    }

    /// <summary>
    /// Maps the inherited SVG <c>fill-rule</c> (presentation attribute or inline style) onto
    /// path and polygon nodes, the only node types where the rule is meaningful. The default
    /// nonzero rule stays null so canonical output omits it.
    /// </summary>
    private static string? ParseFillRule(XElement element, ImportCallContext call)
    {
        if (element.Name.LocalName is not ("path" or "polygon" or "polyline"))
            return null;
        var value = GetCascadedProperty(element, "fill-rule");
        if (value is null)
            return null;
        if (value.Equals(FillRules.EvenOdd, StringComparison.OrdinalIgnoreCase))
            return FillRules.EvenOdd;
        if (!value.Equals(FillRules.NonZero, StringComparison.OrdinalIgnoreCase))
            call.AddWarning(ImportWarningCode.UnsupportedFeature, $"SVG fill-rule '{value}' is not supported; using nonzero", "fill-rule");
        return null;
    }

    private static Fill? ParseFill(XElement element, ImportCallContext call)
    {
        var fillValue = GetCascadedProperty(element, "fill") ?? "#000000";
        if (fillValue.Equals("none", StringComparison.OrdinalIgnoreCase))
            return null;
        var fillOpacity = Math.Clamp(ParseDouble(GetCascadedProperty(element, "fill-opacity"), 1), 0, 1);

        if (fillValue.StartsWith("url(#", StringComparison.Ordinal) && fillValue.EndsWith(')'))
        {
            var gradientId = fillValue[5..^1];
            if (call.Gradients.TryGetValue(gradientId, out var gradient))
                return new Fill { Gradient = CloneGradient(gradient, fillOpacity, call) };
            call.AddWarning(ImportWarningCode.MissingReference, $"Gradient reference '{gradientId}' not found", gradientId);
            return new Fill { Color = "#000000" };
        }

        return new Fill { Color = ApplyColorOpacity(NormalizeColor(fillValue), fillOpacity, call, "fill-opacity") };
    }

    private static Stroke? ParseStroke(XElement element, ImportCallContext call)
    {
        var strokeValue = GetCascadedProperty(element, "stroke");
        if (strokeValue is null || strokeValue.Equals("none", StringComparison.OrdinalIgnoreCase))
            return null;

        var strokeOpacity = Math.Clamp(ParseDouble(GetCascadedProperty(element, "stroke-opacity"), 1), 0, 1);
        return new Stroke
        {
            Color = ApplyColorOpacity(NormalizeColor(strokeValue), strokeOpacity, call, "stroke-opacity"),
            Width = ParseDouble(GetCascadedProperty(element, "stroke-width"), 1),
            Cap = ParseStrokeCap(GetCascadedProperty(element, "stroke-linecap")),
            Join = ParseStrokeJoin(GetCascadedProperty(element, "stroke-linejoin")),
            MiterLimit = ParseDouble(GetCascadedProperty(element, "stroke-miterlimit"), 4),
            DashArray = ParseDashArray(GetCascadedProperty(element, "stroke-dasharray")),
            DashOffset = ParseDouble(GetCascadedProperty(element, "stroke-dashoffset"), 0)
        };
    }

    private static StrokeCap ParseStrokeCap(string? value) => value?.ToLowerInvariant() switch
    {
        "round" => StrokeCap.Round,
        "square" => StrokeCap.Square,
        _ => StrokeCap.Butt
    };

    private static StrokeJoin ParseStrokeJoin(string? value) => value?.ToLowerInvariant() switch
    {
        "round" => StrokeJoin.Round,
        "bevel" => StrokeJoin.Bevel,
        _ => StrokeJoin.Miter
    };

    private static double[]? ParseDashArray(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("none", StringComparison.OrdinalIgnoreCase))
            return null;
        var values = new List<double>();
        foreach (var part in value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number) || number < 0)
                return null;
            values.Add(number);
        }
        return values.Count == 0 ? null : values.ToArray();
    }

    private static string? ParseBlendMode(XElement element)
    {
        var mode = GetLocalProperty(element, "mix-blend-mode");
        return mode is null || mode.Equals("normal", StringComparison.OrdinalIgnoreCase) ? null : mode;
    }

    private static Transform? CreateTransform(double x, double y, XElement element, ImportCallContext call)
    {
        var matrix = ParseTransformMatrix(element, call) * AffineMatrix.Translation(x, y);
        return DecomposeTransform(matrix, call);
    }

    private static Transform? ParseTransformAttribute(XElement element, ImportCallContext call) =>
        DecomposeTransform(ParseTransformMatrix(element, call), call);

    private static AffineMatrix ParseTransformMatrix(XElement element, ImportCallContext call)
    {
        var transformText = element.Attribute("transform")?.Value;
        if (string.IsNullOrWhiteSpace(transformText))
            return AffineMatrix.Identity;

        var result = AffineMatrix.Identity;
        var consumed = 0;
        var foundFunction = false;
        foreach (Match match in TransformFunctionPattern().Matches(transformText))
        {
            var separator = transformText.AsSpan(consumed, match.Index - consumed);
            if ((!foundFunction && !OnlyTransformWhitespace(separator))
                || (foundFunction && !IsValidTransformSeparator(separator)))
            {
                call.AddWarning(ImportWarningCode.ParseError, "Malformed SVG transform was ignored", "transform");
                return AffineMatrix.Identity;
            }
            foundFunction = true;
            consumed = match.Index + match.Length;
            if (!TryParseNumberList(match.Groups[2].Value, out var arguments))
            {
                call.AddWarning(ImportWarningCode.ParseError, "Malformed SVG transform arguments were ignored", "transform");
                return AffineMatrix.Identity;
            }

            var name = match.Groups[1].Value;
            AffineMatrix function;
            switch (name)
            {
                case "translate" when arguments.Length is 1 or 2:
                    function = AffineMatrix.Translation(arguments[0], arguments.Length == 2 ? arguments[1] : 0);
                    break;
                case "scale" when arguments.Length is 1 or 2:
                    function = AffineMatrix.Scale(arguments[0], arguments.Length == 2 ? arguments[1] : arguments[0]);
                    break;
                case "rotate" when arguments.Length is 1 or 3:
                    function = arguments.Length == 1
                        ? AffineMatrix.Rotation(arguments[0])
                        : AffineMatrix.Translation(arguments[1], arguments[2])
                            * AffineMatrix.Rotation(arguments[0])
                            * AffineMatrix.Translation(-arguments[1], -arguments[2]);
                    break;
                case "matrix" when arguments.Length == 6:
                    function = new AffineMatrix(arguments[0], arguments[1], arguments[2], arguments[3], arguments[4], arguments[5]);
                    call.AddWarning(ImportWarningCode.UnsupportedFeature, "SVG matrix transform was decomposed into modeled translate/rotate/scale", "matrix");
                    break;
                case "skewX" when arguments.Length == 1:
                    function = AffineMatrix.SkewX(arguments[0]);
                    break;
                case "skewY" when arguments.Length == 1:
                    function = AffineMatrix.SkewY(arguments[0]);
                    break;
                default:
                    call.AddWarning(ImportWarningCode.UnsupportedFeature, $"SVG {name} transform is malformed or unsupported", name);
                    continue;
            }

            // SVG transform functions are composed in lexical source order.
            result *= function;
            ValidateTransformMatrix(result);
        }

        if (!foundFunction || !OnlyTransformWhitespace(transformText.AsSpan(consumed)))
        {
            call.AddWarning(ImportWarningCode.ParseError, "Malformed SVG transform tail was ignored", "transform");
            return AffineMatrix.Identity;
        }
        return result;
    }

    private static Transform? DecomposeTransform(AffineMatrix matrix, ImportCallContext call)
    {
        ValidateTransformMatrix(matrix);
        if (matrix.IsIdentity)
            return null;

        var scaleX = Math.Sqrt(matrix.A * matrix.A + matrix.B * matrix.B);
        if (scaleX < 1e-12)
        {
            call.AddWarning(ImportWarningCode.UnsupportedFeature, "Degenerate SVG transform cannot be represented exactly", "transform-lossy");
            return new Transform { X = matrix.E, Y = matrix.F, ScaleX = 0, ScaleY = 0 };
        }

        var rotation = Math.Atan2(matrix.B, matrix.A);
        var scaleY = (matrix.A * matrix.D - matrix.B * matrix.C) / scaleX;
        var expectedC = -Math.Sin(rotation) * scaleY;
        var expectedD = Math.Cos(rotation) * scaleY;
        var tolerance = 1e-8 * Math.Max(1, Math.Max(Math.Abs(matrix.C), Math.Abs(matrix.D)));
        if (Math.Abs(matrix.C - expectedC) > tolerance || Math.Abs(matrix.D - expectedD) > tolerance)
            call.AddWarning(ImportWarningCode.UnsupportedFeature, "SVG transform contains skew and was approximated as translate/rotate/scale", "transform-lossy");

        return new Transform
        {
            X = NormalizeZero(matrix.E),
            Y = NormalizeZero(matrix.F),
            Rotation = NormalizeZero(rotation * 180 / Math.PI),
            ScaleX = NormalizeZero(scaleX),
            ScaleY = NormalizeZero(scaleY)
        };
    }

    private static bool TryParseNumberList(string value, out double[] numbers)
    {
        var result = new List<double>();
        var index = 0;
        var afterComma = false;
        while (index < value.Length)
        {
            if (char.IsWhiteSpace(value[index]))
            {
                index++;
                continue;
            }
            if (value[index] == ',')
            {
                if (result.Count == 0 || afterComma)
                {
                    numbers = [];
                    return false;
                }
                afterComma = true;
                index++;
                continue;
            }
            var match = NumberPattern().Match(value, index);
            if (!match.Success
                || !double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                || !double.IsFinite(number))
            {
                numbers = [];
                return false;
            }
            result.Add(number);
            afterComma = false;
            index += match.Length;
        }
        numbers = result.ToArray();
        return !afterComma;
    }

    private static bool OnlyTransformWhitespace(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (!IsSvgWhitespace(character))
                return false;
        }
        return true;
    }

    private static bool IsValidTransformSeparator(ReadOnlySpan<char> value)
    {
        var commaCount = 0;
        var whitespaceCount = 0;
        foreach (var character in value)
        {
            if (character == ',')
            {
                if (++commaCount > 1)
                    return false;
            }
            else if (IsSvgWhitespace(character))
            {
                whitespaceCount++;
            }
            else
            {
                return false;
            }
        }
        return commaCount == 1 || whitespaceCount > 0;
    }

    private static bool IsSvgWhitespace(char character) => character is ' ' or '\t' or '\r' or '\n';

    private static void ValidateTransformMatrix(AffineMatrix matrix)
    {
        if (!matrix.IsFinite
            || matrix.MaxMagnitude > MaxTransformMagnitude)
        {
            throw new SvgResourceLimitException(
                $"SVG transform exceeds finite coefficient limit ({MaxTransformMagnitude.ToString(CultureInfo.InvariantCulture)})");
        }
    }

    private static bool ParseVisibility(XElement element)
    {
        foreach (var current in element.AncestorsAndSelf())
        {
            if (GetLocalProperty(current, "display")?.Equals("none", StringComparison.OrdinalIgnoreCase) == true)
                return false;
        }
        var visibility = GetCascadedProperty(element, "visibility");
        return !string.Equals(visibility, "hidden", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(visibility, "collapse", StringComparison.OrdinalIgnoreCase);
    }

    private static SvgViewport ParseViewport(XElement svg, ImportCallContext call)
    {
        var viewBox = svg.Attribute("viewBox")?.Value;
        if (viewBox is not null && TryParseNumberList(viewBox, out var values) && values.Length == 4
            && values[2] > 0 && values[3] > 0)
        {
            var widthAttribute = ParseDimension(svg.Attribute("width")?.Value, values[2]);
            var heightAttribute = ParseDimension(svg.Attribute("height")?.Value, values[3]);
            if (Math.Abs(widthAttribute - values[2]) > 1e-9 || Math.Abs(heightAttribute - values[3]) > 1e-9)
            {
                call.AddWarning(
                    ImportWarningCode.UnsupportedFeature,
                    "SVG viewport scaling/preserveAspectRatio is not modeled; viewBox dimensions are used",
                    "viewBox-scale");
            }
            return new SvgViewport(values[0], values[1], values[2], values[3]);
        }
        if (viewBox is not null)
            call.AddWarning(ImportWarningCode.ParseError, "Invalid SVG viewBox ignored", "viewBox");
        return new SvgViewport(
            0,
            0,
            ParseDimension(svg.Attribute("width")?.Value, 100),
            ParseDimension(svg.Attribute("height")?.Value, 100));
    }

    private static void ValidateViewport(SvgViewport viewport)
    {
        if (!double.IsFinite(viewport.MinX) || !double.IsFinite(viewport.MinY)
            || !double.IsFinite(viewport.Width) || !double.IsFinite(viewport.Height)
            || viewport.Width <= 0 || viewport.Height <= 0)
        {
            throw new SvgResourceLimitException("SVG viewport must contain finite positive dimensions");
        }
        if (viewport.Width > MaxDimension || viewport.Height > MaxDimension
            || Math.Abs(viewport.MinX) > MaxDimension || Math.Abs(viewport.MinY) > MaxDimension)
        {
            throw new SvgResourceLimitException($"SVG viewport exceeds coordinate/dimension limit ({MaxDimension.ToString(CultureInfo.InvariantCulture)})");
        }
    }

    private static double ParseDimension(string? value, double defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;
        value = value.Trim();
        if (value.EndsWith('%'))
            return defaultValue;
        if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            value = value[..^2];
        return ParseDouble(value, defaultValue);
    }

    private static string? ParseBackground(XElement svg)
    {
        var background = GetLocalProperty(svg, "background") ?? GetLocalProperty(svg, "background-color");
        return background is null ? null : NormalizeColor(background);
    }

    private static List<Point> ParsePoints(string value)
    {
        if (!TryParseNumberList(value, out var coordinates) || coordinates.Length % 2 != 0)
            return [];
        var points = new List<Point>(coordinates.Length / 2);
        for (var index = 0; index < coordinates.Length; index += 2)
            points.Add(new Point { X = coordinates[index], Y = coordinates[index + 1] });
        return points;
    }

    private static string? GetCascadedProperty(XElement element, string property)
    {
        foreach (var current in element.AncestorsAndSelf())
        {
            var value = GetLocalProperty(current, property);
            if (value is null || value.Equals("inherit", StringComparison.OrdinalIgnoreCase))
                continue;
            return value;
        }
        return null;
    }

    private static string? GetLocalProperty(XElement element, string property)
    {
        var style = element.Attribute("style")?.Value;
        if (style is not null)
        {
            foreach (var declaration in style.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = declaration.IndexOf(':');
                if (separator <= 0 || !declaration[..separator].Trim().Equals(property, StringComparison.OrdinalIgnoreCase))
                    continue;
                var value = declaration[(separator + 1)..].Trim();
                if (value.EndsWith("!important", StringComparison.OrdinalIgnoreCase))
                    value = value[..^10].TrimEnd();
                return value;
            }
        }
        return element.Attribute(property)?.Value;
    }

    private static double ParseDouble(string? value, double defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;
        value = value.Trim();
        if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("pt", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("em", StringComparison.OrdinalIgnoreCase))
            value = value[..^2];
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) && double.IsFinite(result)
            ? result
            : defaultValue;
    }

    private static double ParsePercentOrNumber(string? value, double defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;
        value = value.Trim();
        if (value.EndsWith('%')
            && double.TryParse(value[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var percentage)
            && double.IsFinite(percentage))
            return percentage / 100;
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) && double.IsFinite(result)
            ? result
            : defaultValue;
    }

    private static double ParseFontSize(string value)
    {
        value = value.Trim();
        if (value.EndsWith("pt", StringComparison.OrdinalIgnoreCase))
            return ParseDouble(value[..^2], 12) * 4 / 3;
        if (value.EndsWith("em", StringComparison.OrdinalIgnoreCase))
            return ParseDouble(value[..^2], 1) * 16;
        return ParseDouble(value, 16);
    }

    private static int ParseFontWeight(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 400;
        return value.Trim().ToLowerInvariant() switch
        {
            "normal" => 400,
            "bold" => 700,
            "lighter" => 300,
            "bolder" => 700,
            _ => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var weight)
                ? Math.Clamp(weight, 100, 900)
                : 400
        };
    }

    private static string NormalizeColor(string color)
    {
        color = color.Trim();
        if ((color.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase)
                || color.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase))
            && color.EndsWith(')'))
        {
            var rgba = color.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase);
            var start = rgba ? 5 : 4;
            var parts = color[start..^1].Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length == (rgba ? 4 : 3)
                && TryParseRgbChannel(parts[0], out var red)
                && TryParseRgbChannel(parts[1], out var green)
                && TryParseRgbChannel(parts[2], out var blue))
            {
                if (!rgba)
                    return $"#{red:X2}{green:X2}{blue:X2}";
                if (double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var alpha) && double.IsFinite(alpha))
                    return $"#{red:X2}{green:X2}{blue:X2}{(byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255):X2}";
            }
        }
        return color;
    }

    private static bool TryParseRgbChannel(string value, out byte channel)
    {
        channel = 0;
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || !double.IsFinite(parsed))
            return false;
        channel = (byte)Math.Round(Math.Clamp(parsed, 0, 255));
        return true;
    }

    private static string ApplyColorOpacity(string color, double opacity, ImportCallContext call, string warningContext)
    {
        if (opacity >= 1)
            return color;
        if (!TryExpandHexColor(color, out var red, out var green, out var blue, out var alpha))
        {
            call.AddWarning(ImportWarningCode.UnsupportedFeature, $"{warningContext} on non-hex color cannot be represented exactly", warningContext);
            return color;
        }
        var combinedAlpha = (byte)Math.Round(alpha * opacity);
        return $"#{red:X2}{green:X2}{blue:X2}{combinedAlpha:X2}";
    }

    private static bool TryExpandHexColor(string value, out byte red, out byte green, out byte blue, out byte alpha)
    {
        red = green = blue = 0;
        alpha = 255;
        if (!value.StartsWith('#'))
            return false;
        var hex = value[1..];
        if (hex.Length is 3 or 4)
            hex = string.Concat(hex.Select(character => new string(character, 2)));
        if (hex.Length is not (6 or 8)
            || !byte.TryParse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out red)
            || !byte.TryParse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out green)
            || !byte.TryParse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out blue))
            return false;
        return hex.Length != 8 || byte.TryParse(hex[6..8], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out alpha);
    }

    private static Gradient CloneGradient(Gradient gradient, double opacity, ImportCallContext call) => gradient switch
    {
        LinearGradient linear => new LinearGradient
        {
            X1 = linear.X1,
            Y1 = linear.Y1,
            X2 = linear.X2,
            Y2 = linear.Y2,
            Stops = CloneStops(linear.Stops, opacity, call)
        },
        RadialGradient radial => new RadialGradient
        {
            Cx = radial.Cx,
            Cy = radial.Cy,
            R = radial.R,
            Fx = radial.Fx,
            Fy = radial.Fy,
            Stops = CloneStops(radial.Stops, opacity, call)
        },
        _ => throw new InvalidOperationException("Unknown gradient type")
    };

    private static List<GradientStop> CloneStops(
        IEnumerable<GradientStop> source,
        double opacity,
        ImportCallContext call)
    {
        var result = new List<GradientStop>();
        foreach (var stop in source)
        {
            call.ChargeClonedGradientStop();
            result.Add(new GradientStop
            {
                Offset = stop.Offset,
                Color = stop.Color,
                Opacity = Math.Clamp(stop.Opacity * opacity, 0, 1)
            });
        }
        return result;
    }

    private static void WarnForUnsupportedAttributes(XElement element, ImportCallContext call)
    {
        if (element.Attribute("class") is not null)
            call.AddWarning(ImportWarningCode.UnsupportedFeature, "CSS class selectors are not evaluated", "css-class");
        foreach (var property in new[]
        {
            "filter", "mask", "marker-start", "marker-mid", "marker-end",
            "vector-effect", "paint-order", "transform-origin"
        })
        {
            if (GetLocalProperty(element, property) is not null)
                call.AddWarning(ImportWarningCode.UnsupportedFeature, $"SVG {property} is not supported", property);
        }

        if (string.Equals(GetCascadedProperty(element, "fill"), "currentColor", StringComparison.OrdinalIgnoreCase)
            || string.Equals(GetCascadedProperty(element, "stroke"), "currentColor", StringComparison.OrdinalIgnoreCase))
        {
            call.AddWarning(ImportWarningCode.UnsupportedFeature, "SVG currentColor inheritance is not modeled", "currentColor");
        }
        if (element.Name.LocalName == "text")
        {
            foreach (var property in new[]
            {
                "dominant-baseline", "alignment-baseline", "font-style",
                "word-spacing", "text-decoration", "writing-mode", "direction"
            })
            {
                if (GetCascadedProperty(element, property) is not null)
                    call.AddWarning(ImportWarningCode.UnsupportedFeature, $"SVG text property {property} is not modeled", property);
            }
        }
    }

    private static Transform? CreateTranslation(double x, double y) =>
        x == 0 && y == 0 ? null : new Transform { X = x, Y = y };

    private static double NormalizeZero(double value) => Math.Abs(value) < 1e-12 ? 0 : value;

    private static bool IsSvgElement(XElement element, string localName) =>
        element.Name.LocalName == localName
        && (element.Name.Namespace == SvgNs || element.Name.Namespace == XNamespace.None);

    private sealed class ImportCallContext(CancellationToken cancellationToken)
    {
        private int _clonedGradientStops;

        private int _useExpansions;

        public CancellationToken CancellationToken { get; } = cancellationToken;
        public List<ImportWarning> Warnings { get; } = [];
        public Dictionary<string, XElement> GradientElements { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, Gradient> Gradients { get; } = new(StringComparer.Ordinal);

        /// <summary>Every element carrying an <c>id</c>, for <c>&lt;use&gt;</c> and clip-path resolution.</summary>
        public Dictionary<string, XElement> ElementsById { get; } = new(StringComparer.Ordinal);

        /// <summary>Ids currently being expanded, so a <c>&lt;use&gt;</c> reference cycle is caught.</summary>
        public HashSet<string> ExpandingUse { get; } = new(StringComparer.Ordinal);

        public void ChargeUseExpansion()
        {
            CancellationToken.ThrowIfCancellationRequested();
            if (++_useExpansions > MaxUseExpansions)
                throw new SvgResourceLimitException($"SVG <use> expansion count exceeds maximum ({MaxUseExpansions})");
        }

        public void AddWarning(ImportWarningCode code, string message, string? context = null)
        {
            if (!Warnings.Any(warning => warning.Code == code && warning.Context == context))
                Warnings.Add(new ImportWarning(code, message, context));
        }

        public void ChargeClonedGradientStop()
        {
            CancellationToken.ThrowIfCancellationRequested();
            if (++_clonedGradientStops > MaxClonedGradientStops)
            {
                throw new SvgResourceLimitException(
                    $"SVG cloned gradient stop count exceeds maximum ({MaxClonedGradientStops})");
            }
        }
    }

    private readonly record struct SvgViewport(double MinX, double MinY, double Width, double Height);

    private readonly record struct AffineMatrix(double A, double B, double C, double D, double E, double F)
    {
        public static AffineMatrix Identity => new(1, 0, 0, 1, 0, 0);
        public bool IsIdentity => A == 1 && B == 0 && C == 0 && D == 1 && E == 0 && F == 0;
        public bool IsFinite => double.IsFinite(A) && double.IsFinite(B) && double.IsFinite(C)
            && double.IsFinite(D) && double.IsFinite(E) && double.IsFinite(F);
        public double MaxMagnitude => new[] { A, B, C, D, E, F }.Max(Math.Abs);

        public static AffineMatrix Translation(double x, double y) => new(1, 0, 0, 1, x, y);
        public static AffineMatrix Scale(double x, double y) => new(x, 0, 0, y, 0, 0);
        public static AffineMatrix Rotation(double degrees)
        {
            var radians = degrees * Math.PI / 180;
            var cosine = Math.Cos(radians);
            var sine = Math.Sin(radians);
            return new AffineMatrix(cosine, sine, -sine, cosine, 0, 0);
        }
        public static AffineMatrix SkewX(double degrees) => new(1, 0, Math.Tan(degrees * Math.PI / 180), 1, 0, 0);
        public static AffineMatrix SkewY(double degrees) => new(1, Math.Tan(degrees * Math.PI / 180), 0, 1, 0, 0);

        public static AffineMatrix operator *(AffineMatrix left, AffineMatrix right) => new(
            left.A * right.A + left.C * right.B,
            left.B * right.A + left.D * right.B,
            left.A * right.C + left.C * right.D,
            left.B * right.C + left.D * right.D,
            left.A * right.E + left.C * right.F + left.E,
            left.B * right.E + left.D * right.F + left.F);
    }

    private sealed class SvgResourceLimitException(string message) : Exception(message);
}

public sealed record SvgImportOptions
{
    public string? DocumentName { get; init; }
}

public sealed class SvgImportResult
{
    public bool Success { get; init; }
    public Document? Document { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyList<ImportWarning> Warnings { get; init; } = [];

    public static SvgImportResult Fail(string code, string message, IReadOnlyList<ImportWarning>? warnings = null) =>
        new() { Success = false, ErrorCode = code, ErrorMessage = message, Warnings = warnings?.ToList() ?? [] };
}

public sealed record ImportWarning(ImportWarningCode Code, string Message, string? Context);

public enum ImportWarningCode
{
    UnsupportedElement,
    UnsupportedFeature,
    MissingReference,
    ParseError
}
