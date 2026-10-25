using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Export;
using FTPlus.AgentGraphicDesign.Core.Import;
using FTPlus.AgentGraphicDesign.Core.Layout;
using FTPlus.AgentGraphicDesign.Core.Lint;
using FTPlus.AgentGraphicDesign.Core.Patching;
using FTPlus.AgentGraphicDesign.Core.PathOperations;
using FTPlus.AgentGraphicDesign.Core.Rendering;
using FTPlus.AgentGraphicDesign.Core.Serialization;
using FTPlus.AgentGraphicDesign.Core.Tokens;
using FTPlus.AgentGraphicDesign.Core.Validation;
using FTPlus.AgentGraphicDesign.Skia.TextShaping;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace FTPlus.AgentGraphicDesign.Mcp.Tools;

/// <summary>
/// Thin MCP adapter over <see cref="IDocumentEngine"/>: parse tool input, call the
/// engine, format the result. No document logic lives here.
/// All file paths are confined to the working directory to prevent directory traversal.
/// </summary>
[McpServerToolType]
public sealed class DocumentTools
{
    private const int MaxNameLength = 256;
    private const int MaxPersistedValueLength = 1024;
    private const int MaxTextLength = 1_000_000;
    private const long MaxInputFileBytes = 16 * 1024 * 1024;
    private const int MaxRasterDimension = 2048;
    private const long MaxOutputBytes = 64 * 1024 * 1024;

    private readonly IDocumentEngine _engine;
    private readonly PathConfinement _pathConfinement;
    private readonly IRenderer _renderer;

    public DocumentTools(IDocumentEngine engine, PathConfinement pathConfinement, IRenderer renderer)
    {
        _engine = engine;
        _pathConfinement = pathConfinement;
        _renderer = renderer;
    }

    [McpServerTool(Title = "List Documents")]
    [Description("List all .agd.json documents in the working directory")]
    public CallToolResult ListDocuments(CancellationToken cancellationToken = default)
    {
        var documents = new List<object>();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var workingDir = _pathConfinement.WorkingDirectory;
            if (!Directory.Exists(workingDir))
            {
                return ToolResult.Success(new { documents = Array.Empty<object>() });
            }

            foreach (var file in EnumerateDocumentFiles(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var confined = _pathConfinement.Confine(file);
                    if (!confined.Success)
                    {
                        continue;
                    }
                    if (new FileInfo(confined.Path!).Length > MaxInputFileBytes)
                    {
                        continue;
                    }

                    var result = _engine.TryLoadDocumentFromFile(confined.Path!, cancellationToken);
                    if (result.Success && result.Document is not null)
                    {
                        var fileInfo = new FileInfo(file);
                        documents.Add(new
                        {
                            id = result.Document.Id,
                            name = result.Document.Name,
                            locator = _pathConfinement.RelativePath(confined.Path!),
                            revn = result.Document.Revn,
                            updatedAt = fileInfo.LastWriteTimeUtc
                        });
                    }
                }
                catch (IOException)
                {
                    // Skip unreadable files
                }
            }

            return ToolResult.Success(new
            {
                count = documents.Count,
                documents
            });
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return ToolResult.FromException(ex, "LIST_FAILED");
        }
    }

    [McpServerTool(Title = "Create Document")]
    [Description("Create a new vector scene document with the specified dimensions or from a canvas template")]
    public CallToolResult CreateDocument(
        [Description("Name for the document")] string name,
        [Description("Canvas width in pixels (default: 1920, or the template width)")] double? width = null,
        [Description("Canvas height in pixels (default: 1080, or the template height)")] double? height = null,
        [Description("Background color in hex format, e.g. #ffffff; overrides the template background")] string? background = null,
        [Description("Canvas template id: web-icon-32, web-icon-24, social-1200x630 (explicit width/height/background override it)")] string? template = null,
        [Description("Seed for deterministic ID generation; enables reproducible output")] int? seed = null,
        [Description("Overwrite the selected existing document (default: false)")] bool force = false,
        [Description("Optional readable .agd.json output path within the working directory; omitted uses the document ID")] string? outputPath = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxNameLength)
        {
            return ToolResult.Fail(EngineErrors.InvalidProperty,
                $"Name must contain 1-{MaxNameLength} characters");
        }
        if (background?.Length > 1024 || template?.Length > MaxNameLength)
        {
            return ToolResult.Fail("INPUT_TOO_LARGE", "Background or template value exceeds the allowed limit");
        }

        DocumentTemplate? resolvedTemplate = null;
        if (template is not null && !DocumentTemplates.TryGet(template, out resolvedTemplate))
        {
            return ToolResult.Fail(EngineErrors.InvalidArguments,
                $"Unknown template '{template}'. Valid templates: {DocumentTemplates.IdList}");
        }

        var canvas = DocumentTemplates.ResolveCanvas(resolvedTemplate, width, height, background);
        if (!double.IsFinite(canvas.Width) || !double.IsFinite(canvas.Height) ||
            canvas.Width <= 0 || canvas.Height <= 0 || canvas.Width > 100_000 || canvas.Height > 100_000)
        {
            return ToolResult.Fail(EngineErrors.InvalidDimensions,
                "Canvas dimensions must be finite and between 0 and 100000");
        }

        try
        {
            var options = seed.HasValue ? new CreateDocumentOptions { Seed = seed.Value } : null;
            var document = _engine.CreateDocument(name, canvas.Width, canvas.Height, canvas.Background, options);
            if (outputPath is not null && !outputPath.EndsWith(".agd.json", StringComparison.OrdinalIgnoreCase))
            {
                return ToolResult.Fail(EngineErrors.InvalidArguments, "Document output path must end with .agd.json");
            }
            var pathResult = _pathConfinement.Confine(outputPath ?? $"{document.Id:D}.agd.json");
            if (!pathResult.Success)
            {
                return ToolResult.Fail(EngineErrors.PathEscape, pathResult.Error!);
            }
            if (outputPath is not null)
            {
                SaveCreatedDocument(document, pathResult.Path!, force, cancellationToken);
            }
            else if (force)
            {
                _engine.SaveDocument(document, expectedRevn: null, cancellationToken);
            }
            else
            {
                _engine.CreateDocumentInStore(document, cancellationToken);
            }

            return ToolResult.Success(new
            {
                id = document.Id,
                locator = outputPath is null ? document.Id.ToString() : _pathConfinement.RelativePath(pathResult.Path!),
                rootId = document.Root.Id,
                revn = document.Revn,
                name = document.Name,
                template = resolvedTemplate?.Id,
                canvas = new { width = document.Canvas.Width, height = document.Canvas.Height, background = document.Canvas.Background }
            });
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return ToolResult.FromException(ex, "CREATE_FAILED");
        }
    }

    [McpServerTool(Title = "Load Document")]
    [Description("Load an existing document by ID or file path (confined to working directory)")]
    public CallToolResult LoadDocument(
        [Description("Document ID (GUID) or file path within working directory")] string idOrPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (handle, error) = OpenConfined(idOrPath, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (handle is null)
        {
            return DocumentNotFound(idOrPath);
        }

        var document = handle.Document;
        return ToolResult.Success(new
        {
            id = document.Id,
            name = document.Name,
            version = document.Version,
            revn = document.Revn,
            canvas = new
            {
                width = document.Canvas.Width,
                height = document.Canvas.Height,
                background = document.Canvas.Background
            },
            nodeCount = _engine.CountNodes(document)
        });
    }

    [McpServerTool(Title = "Validate Document")]
    [Description("Validate a document and return any errors or warnings")]
    public CallToolResult ValidateDocument(
        [Description("Document ID (GUID) or file path within working directory")] string idOrPath,
        [Description("Treat validation warnings as invalid")] bool strict = false,
        [Description("Additional validation profile: web-icon")] string? profile = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ValidationProfiles.TryParse(profile, out var validationProfile))
        {
            return ToolResult.Fail(EngineErrors.InvalidArguments,
                $"Unknown validation profile. Supported: {ValidationProfiles.WebIcon}");
        }

        var (handle, error) = OpenConfined(idOrPath, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (handle is null)
        {
            return DocumentNotFound(idOrPath);
        }

        var result = _engine.Validate(handle.Document, new ValidationOptions { Profile = validationProfile });

        return ToolResult.Success(new
        {
            valid = result.IsValid && (!strict || result.Warnings.Count == 0),
            strict,
            errors = result.Errors.Select(e => new { e.Code, e.Message, e.Path }),
            warnings = result.Warnings.Select(w => new { w.Code, w.Message, w.Path })
        });
    }

    public CallToolResult ValidateDocument(
        string idOrPath,
        bool strict,
        CancellationToken cancellationToken) =>
        ValidateDocument(idOrPath, strict, profile: null, cancellationToken: cancellationToken);

    [McpServerTool(Title = "List Nodes", ReadOnly = true)]
    [Description("Inspect a bounded page of document-order node summaries, optionally restricted to a subtree and relative depth")]
    public CallToolResult ListNodes(
        [Description("Document ID (GUID) or file path within working directory")] string idOrPath,
        [Description("Optional subtree root node ID (GUID)")] string? nodeId = null,
        [Description("Page offset in the filtered selection (default: 0)")] int offset = 0,
        [Description("Maximum returned summaries (default: 100, range 1-1000)")] int limit = 100,
        [Description("Optional maximum depth relative to the selected root (0 returns only that root)")] int? maxDepth = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (offset is < 0 or > 100_000 || limit is < 1 or > 1000 || maxDepth is < 0 or > 128)
        {
            return ToolResult.Fail(EngineErrors.InvalidArguments, "Invalid node page offset, limit or relative depth");
        }
        var (handle, error) = OpenConfined(idOrPath, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (handle is null)
        {
            return DocumentNotFound(idOrPath);
        }

        Guid? rootId = null;
        if (nodeId is not null)
        {
            if (!Guid.TryParse(nodeId, out var id))
            {
                return ToolResult.Fail(EngineErrors.NodeNotFound, "Invalid subtree node ID");
            }
            rootId = id;
        }
        try
        {
            var page = _engine.ListNodesPage(handle.Document, new NodePageQuery(rootId, offset, limit, maxDepth), cancellationToken);
            return ToolResult.Success(new
            {
                documentId = handle.Document.Id,
                documentName = handle.Document.Name,
                documentRevn = handle.Document.Revn,
                rootId = page.RootId,
                total = page.Total,
                offset,
                limit,
                hasMore = page.HasMore,
                nodes = page.Nodes.Select(n => new { id = n.Id, type = n.Type, name = n.Name, path = n.Path, order = n.Order, depth = n.Depth, visible = n.Visible }).ToArray()
            });
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return ToolResult.FromException(ex, "LIST_FAILED");
        }
    }

    [McpServerTool(Title = "Add Node", ReadOnly = false)]
    [Description("Add a new node to a document")]
    public CallToolResult AddNode(
        [Description("Document ID (GUID) or file path within working directory")] string idOrPath,
        [Description("Parent node ID (GUID), or 'root' for root level")] string parentId,
        [Description("Node type. Accepted values: rectangle (alias: rect), ellipse (alias: circle), text, path, group, frame, line, polygon (alias: poly), image (alias: img)")] string type,
        [Description("Node name")] string? name = null,
        [Description("Width (for rectangle, ellipse, frame, image)")] double? width = null,
        [Description("Height (for rectangle, ellipse, frame, image)")] double? height = null,
        [Description("X position")] double x = 0,
        [Description("Y position")] double y = 0,
        [Description("Fill color in hex format")] string? fill = null,
        [Description("Stroke color in hex format")] string? stroke = null,
        [Description("Fill color token reference")] string? fillToken = null,
        [Description("Stroke color token reference")] string? strokeToken = null,
        [Description("Text content (for text nodes)")] string? text = null,
        [Description("Path data SVG d attribute (for path nodes)")] string? pathData = null,
        [Description("Corner radius (for rectangle nodes)")] double? cornerRadius = null,
        [Description("Font family (for text nodes)")] string? fontFamily = null,
        [Description("Font size in pixels (for text nodes)")] double? fontSize = null,
        [Description("Font weight 100-900 (for text nodes)")] int? fontWeight = null,
        [Description("Typography token reference (for text nodes)")] string? typographyToken = null,
        [Description("Opacity 0.0-1.0")] double? opacity = null,
        [Description("Fill rule: nonzero or evenodd (for path and polygon nodes)")] string? fillRule = null,
        [Description("Line start X (for line nodes)")] double? x1 = null,
        [Description("Line start Y (for line nodes)")] double? y1 = null,
        [Description("Line end X (for line nodes)")] double? x2 = null,
        [Description("Line end Y (for line nodes)")] double? y2 = null,
        [Description("Points as 'x1,y1 x2,y2 ...' string (for polygon nodes)")] string? points = null,
        [Description("Whether polygon is closed (for polygon nodes, default: true)")] bool? polygonClosed = null,
        [Description("Image source URL or path (for image nodes)")] string? imageSrc = null,
        [Description("Horizontal alignment relative to x: start, center, end (for text nodes)")] string? textAlign = null,
        [Description("Vertical anchor at y: top, middle, bottom, baseline (for text nodes)")] string? verticalAlign = null,
        [Description("Wrap width in pixels; words wrap so no line exceeds it (for text nodes)")] double? maxWidth = null,
        [Description("Line height as a multiple of the font size, default 1.2 (for text nodes)")] double? lineHeight = null,
        [Description("Extra spacing after each glyph in pixels (for text nodes)")] double? letterSpacing = null,
        [Description("Expected revision for optimistic concurrency (optional)")] long? expectedRevn = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!AllFinite(width, height) || width is < 0 || height is < 0)
        {
            return ToolResult.Fail(EngineErrors.InvalidDimensions,
                "Node dimensions must be finite and non-negative");
        }
        if (!AllFinite(x, y, cornerRadius, fontSize, opacity, x1, y1, x2, y2, maxWidth, lineHeight, letterSpacing) ||
            cornerRadius is < 0 || fontSize is <= 0 || maxWidth is <= 0 || lineHeight is <= 0 ||
            fontWeight is < 100 or > 900 || opacity is < 0 or > 1 || expectedRevn is < 0)
        {
            return ToolResult.Fail(EngineErrors.InvalidProperty,
                "One or more numeric values are non-finite or outside the allowed range");
        }
        if (TooLong(type, MaxNameLength) || TooLong(name, MaxNameLength) ||
            TooLong(fill, MaxPersistedValueLength) || TooLong(stroke, MaxPersistedValueLength) ||
            TooLong(fillToken, MaxPersistedValueLength) || TooLong(strokeToken, MaxPersistedValueLength) ||
            TooLong(typographyToken, MaxPersistedValueLength) || TooLong(fillRule, MaxPersistedValueLength) ||
            TooLong(textAlign, MaxNameLength) || TooLong(verticalAlign, MaxNameLength) ||
            TooLong(fontFamily, MaxPersistedValueLength) || TooLong(text) || TooLong(pathData) ||
            TooLong(points) || TooLong(imageSrc))
        {
            return ToolResult.Fail("INPUT_TOO_LARGE", "One or more text inputs exceed the allowed limit");
        }

        var (normalizedImageSrc, imageSourceError) = ConfineImageSource(imageSrc);
        if (imageSourceError is not null)
        {
            return imageSourceError;
        }

        var (handle, error) = OpenConfined(idOrPath, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (handle is null)
        {
            return DocumentNotFound(idOrPath);
        }

        Guid? parentGuid = null;
        if (!parentId.Equals("root", StringComparison.OrdinalIgnoreCase))
        {
            if (!Guid.TryParse(parentId, out var parsed))
            {
                return ToolResult.Fail(EngineErrors.ParentNotFound, $"Parent node not found: {parentId}");
            }

            parentGuid = parsed;
        }

        try
        {
            var node = _engine.AddNode(handle.Document, parentGuid, new NewNodeSpec
            {
                Type = type,
                Name = name,
                Width = width,
                Height = height,
                X = x,
                Y = y,
                Fill = fill,
                Stroke = stroke,
                FillTokenRef = fillToken,
                StrokeTokenRef = strokeToken,
                Text = text,
                PathData = pathData,
                CornerRadius = cornerRadius,
                FontFamily = fontFamily,
                FontSize = fontSize,
                FontWeight = fontWeight,
                TypographyTokenRef = typographyToken,
                TextAlign = textAlign,
                VerticalAlign = verticalAlign,
                MaxWidth = maxWidth,
                LineHeight = lineHeight,
                LetterSpacing = letterSpacing,
                Opacity = opacity,
                FillRule = fillRule,
                X1 = x1,
                Y1 = y1,
                X2 = x2,
                Y2 = y2,
                Points = points,
                PolygonClosed = polygonClosed,
                ImageSrc = normalizedImageSrc
            });

            Save(handle, expectedRevn, cancellationToken);

            return ToolResult.Success(new
            {
                nodeId = node.Id,
                nodeType = type,
                nodeName = node.Name,
                documentRevn = handle.Document.Revn
            });
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return ToolResult.FromException(ex, "ADD_FAILED");
        }
    }

    [McpServerTool(Title = "Update Node", ReadOnly = false)]
    [Description("Update properties of an existing node. Use '__clear__' for fill/stroke to remove them, for fillRule to reset it, or for textAlign/verticalAlign to restore the default.")]
    public CallToolResult UpdateNode(
        [Description("Document ID (GUID) or file path within working directory")] string idOrPath,
        [Description("Node ID (GUID) to update")] string nodeId,
        [Description("New name (optional)")] string? name = null,
        [Description("New width (optional)")] double? width = null,
        [Description("New height (optional)")] double? height = null,
        [Description("New X position (optional)")] double? x = null,
        [Description("New Y position (optional)")] double? y = null,
        [Description("New fill color, or '__clear__' to remove (optional)")] string? fill = null,
        [Description("New stroke color, or '__clear__' to remove (optional)")] string? stroke = null,
        [Description("New fill color token reference (optional)")] string? fillToken = null,
        [Description("New stroke color token reference (optional)")] string? strokeToken = null,
        [Description("New visibility (optional)")] bool? visible = null,
        [Description("New text content (text nodes only)")] string? text = null,
        [Description("New path d attribute (path nodes only)")] string? pathData = null,
        [Description("New corner radius (rectangle nodes only)")] double? cornerRadius = null,
        [Description("New font family (text nodes only)")] string? fontFamily = null,
        [Description("New font size in pixels (text nodes only)")] double? fontSize = null,
        [Description("New font weight 100-900 (text nodes only)")] int? fontWeight = null,
        [Description("New typography token reference, or '__clear__' to detach (text nodes only)")] string? typographyToken = null,
        [Description("New opacity 0.0-1.0 (optional)")] double? opacity = null,
        [Description("New fill rule: nonzero or evenodd, or '__clear__' to reset (path and polygon nodes only)")] string? fillRule = null,
        [Description("New horizontal alignment: start, center, end, or '__clear__' for the default (text nodes only)")] string? textAlign = null,
        [Description("New vertical anchor: top, middle, bottom, baseline, or '__clear__' for the default (text nodes only)")] string? verticalAlign = null,
        [Description("New wrap width in pixels (text nodes only)")] double? maxWidth = null,
        [Description("New line height as a multiple of the font size (text nodes only)")] double? lineHeight = null,
        [Description("New extra spacing after each glyph in pixels (text nodes only)")] double? letterSpacing = null,
        [Description("Expected revision for optimistic concurrency (optional)")] long? expectedRevn = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!AllFinite(width, height) || width is < 0 || height is < 0)
        {
            return ToolResult.Fail(EngineErrors.InvalidDimensions,
                "Node dimensions must be finite and non-negative");
        }
        if (!AllFinite(x, y, cornerRadius, fontSize, opacity, maxWidth, lineHeight, letterSpacing) ||
            cornerRadius is < 0 || fontSize is <= 0 || maxWidth is <= 0 || lineHeight is <= 0 ||
            fontWeight is < 100 or > 900 || opacity is < 0 or > 1 || expectedRevn is < 0)
        {
            return ToolResult.Fail(EngineErrors.InvalidProperty,
                "One or more numeric values are non-finite or outside the allowed range");
        }
        if (TooLong(name, MaxNameLength) || TooLong(fill, MaxPersistedValueLength) ||
            TooLong(stroke, MaxPersistedValueLength) || TooLong(fontFamily, MaxPersistedValueLength) ||
            TooLong(fillToken, MaxPersistedValueLength) || TooLong(strokeToken, MaxPersistedValueLength) ||
            TooLong(typographyToken, MaxPersistedValueLength) || TooLong(fillRule, MaxPersistedValueLength) ||
            TooLong(textAlign, MaxNameLength) || TooLong(verticalAlign, MaxNameLength) ||
            TooLong(text) || TooLong(pathData))
        {
            return ToolResult.Fail("INPUT_TOO_LARGE", "One or more text inputs exceed the allowed limit");
        }

        var (handle, error) = OpenConfined(idOrPath, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (handle is null)
        {
            return DocumentNotFound(idOrPath);
        }

        if (!Guid.TryParse(nodeId, out var nodeGuid))
        {
            return ToolResult.Fail(EngineErrors.NodeNotFound, $"Node not found: {nodeId}");
        }

        try
        {
            var node = _engine.UpdateNode(handle.Document, nodeGuid, new NodeUpdates
            {
                Name = name,
                Width = width,
                Height = height,
                X = x,
                Y = y,
                Fill = fill,
                Stroke = stroke,
                FillTokenRef = fillToken,
                StrokeTokenRef = strokeToken,
                Visible = visible,
                Text = text,
                PathData = pathData,
                CornerRadius = cornerRadius,
                FontFamily = fontFamily,
                FontSize = fontSize,
                FontWeight = fontWeight,
                TypographyTokenRef = typographyToken,
                TextAlign = textAlign,
                VerticalAlign = verticalAlign,
                MaxWidth = maxWidth,
                LineHeight = lineHeight,
                LetterSpacing = letterSpacing,
                Opacity = opacity,
                FillRule = fillRule
            });

            Save(handle, expectedRevn, cancellationToken);

            return ToolResult.Success(new
            {
                nodeId = node.Id,
                nodeName = node.Name,
                documentRevn = handle.Document.Revn
            });
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return ToolResult.FromException(ex, "UPDATE_FAILED");
        }
    }

    [McpServerTool(Title = "Delete Node", ReadOnly = false)]
    [Description("Delete a node from a document")]
    public CallToolResult DeleteNode(
        [Description("Document ID (GUID) or file path within working directory")] string idOrPath,
        [Description("Node ID (GUID) to delete")] string nodeId,
        [Description("Expected revision for optimistic concurrency (optional)")] long? expectedRevn = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (expectedRevn is < 0)
        {
            return ToolResult.Fail(EngineErrors.InvalidProperty, "Expected revision cannot be negative");
        }

        var (handle, error) = OpenConfined(idOrPath, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (handle is null)
        {
            return DocumentNotFound(idOrPath);
        }

        if (!Guid.TryParse(nodeId, out var nodeGuid))
        {
            return ToolResult.Fail(EngineErrors.NodeNotFound, $"Node not found: {nodeId}");
        }

        try
        {
            _engine.RemoveNode(handle.Document, nodeGuid);
            Save(handle, expectedRevn, cancellationToken);

            return ToolResult.Success(new
            {
                deleted = nodeId,
                documentRevn = handle.Document.Revn
            });
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return ToolResult.FromException(ex, "DELETE_FAILED");
        }
    }

    [McpServerTool(Title = "Move Node", ReadOnly = false)]
    [Description("Move a node to a new parent container")]
    public CallToolResult MoveNode(
        [Description("Document ID (GUID) or file path within working directory")] string idOrPath,
        [Description("Node ID (GUID) to move")] string nodeId,
        [Description("New parent node ID (GUID), or 'root' for root level")] string newParentId,
        [Description("Expected revision for optimistic concurrency (optional)")] long? expectedRevn = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (expectedRevn is < 0)
        {
            return ToolResult.Fail(EngineErrors.InvalidProperty, "Expected revision cannot be negative");
        }

        var (handle, error) = OpenConfined(idOrPath, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (handle is null)
        {
            return DocumentNotFound(idOrPath);
        }

        if (!Guid.TryParse(nodeId, out var nodeGuid))
        {
            return ToolResult.Fail(EngineErrors.NodeNotFound, $"Node not found: {nodeId}");
        }

        Guid? parentGuid = null;
        if (!newParentId.Equals("root", StringComparison.OrdinalIgnoreCase))
        {
            if (!Guid.TryParse(newParentId, out var parsed))
            {
                return ToolResult.Fail(EngineErrors.ParentNotFound, $"Parent node not found: {newParentId}");
            }

            parentGuid = parsed;
        }

        try
        {
            var node = _engine.MoveNode(handle.Document, nodeGuid, parentGuid);
            Save(handle, expectedRevn, cancellationToken);

            return ToolResult.Success(new
            {
                nodeId = node.Id,
                nodeName = node.Name,
                newOrder = node.Order,
                documentRevn = handle.Document.Revn
            });
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return ToolResult.FromException(ex, "MOVE_FAILED");
        }
    }

    [McpServerTool(Title = "Reorder Node", ReadOnly = false)]
    [Description("Reorder a node among its siblings using conventional before/after semantics.")]
    public CallToolResult ReorderNode(
        [Description("Document ID (GUID) or file path within working directory")] string idOrPath,
        [Description("Node ID (GUID) to reorder")] string nodeId,
        [Description("Place immediately before this sibling node ID (optional)")] string? beforeNodeId = null,
        [Description("Place immediately after this sibling node ID (optional)")] string? afterNodeId = null,
        [Description("Expected revision for optimistic concurrency (optional)")] long? expectedRevn = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (beforeNodeId is null && afterNodeId is null)
        {
            return ToolResult.Fail(EngineErrors.InvalidProperty,
                "Specify beforeNodeId, afterNodeId, or both");
        }
        if (expectedRevn is < 0)
        {
            return ToolResult.Fail(EngineErrors.InvalidProperty, "Expected revision cannot be negative");
        }

        var (handle, error) = OpenConfined(idOrPath, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (handle is null)
        {
            return DocumentNotFound(idOrPath);
        }

        if (!Guid.TryParse(nodeId, out var nodeGuid))
        {
            return ToolResult.Fail(EngineErrors.NodeNotFound, $"Node not found: {nodeId}");
        }

        Guid? beforeGuid = null;
        if (beforeNodeId is not null)
        {
            if (!Guid.TryParse(beforeNodeId, out var parsed))
            {
                return ToolResult.Fail(EngineErrors.NodeNotFound, $"Before node not found: {beforeNodeId}");
            }
            beforeGuid = parsed;
        }

        Guid? afterGuid = null;
        if (afterNodeId is not null)
        {
            if (!Guid.TryParse(afterNodeId, out var parsed))
            {
                return ToolResult.Fail(EngineErrors.NodeNotFound, $"After node not found: {afterNodeId}");
            }
            afterGuid = parsed;
        }

        try
        {
            var node = _engine.ReorderNode(handle.Document, nodeGuid, afterGuid, beforeGuid);
            Save(handle, expectedRevn, cancellationToken);

            return ToolResult.Success(new
            {
                nodeId = node.Id,
                nodeName = node.Name,
                newOrder = node.Order,
                documentRevn = handle.Document.Revn
            });
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return ToolResult.FromException(ex, "REORDER_FAILED");
        }
    }

    [McpServerTool(Title = "Apply Patch", ReadOnly = false)]
    [Description("Apply many edits to a document atomically in one call and one revision. 'operations' is a JSON array " +
        "(max 500 items) of objects with an 'op' field: " +
        "set {nodeId, path, value} sets any node property by JSON Pointer relative to the node, e.g. /style/fill/color, " +
        "/transform/rotation, /style/stroke/dashArray, /points/1/x, /src (creates missing objects; arrays by index or '-' to append; /id, /type and /children paths are rejected, address children by their own nodeId); " +
        "unset {nodeId, path} removes a property (same path rules); " +
        "setNode {nodeId, node} replaces a whole node (id must match; a container's children are replaced too); " +
        "addNode {node, parentId?, afterId?} inserts node JSON (missing id/order are generated; the new id is returned in createdIds); " +
        "deleteNode {nodeId}; moveNode {nodeId, parentId?, afterId?}; reorderNode {nodeId, afterId} (null = first); " +
        "setColorToken {name, value}; removeColorToken {name}; setCanvas {width?, height?, background?} (background null clears). " +
        "All operations succeed or none is saved; errors report the failing operationIndex.")]
    public CallToolResult ApplyPatch(
        [Description("Document ID (GUID) or file path within working directory")] string idOrPath,
        [Description("JSON array of patch operation objects (not a JSON-encoded string)")] JsonElement[] operations,
        [Description("Expected revision for optimistic concurrency (optional)")] long? expectedRevn = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (expectedRevn is < 0)
        {
            return ToolResult.Fail(EngineErrors.InvalidProperty, "Expected revision cannot be negative");
        }

        if (operations is null)
        {
            return ToolResult.Fail(EngineErrors.InvalidArguments, "Operations must be a JSON array");
        }
        if (operations.Length > PatchLimits.MaxOperations)
        {
            return ToolResult.Fail(EngineErrors.ResourceLimit, "Too many patch operations");
        }
        long bytes = 2 + Math.Max(0, operations.Length - 1);
        foreach (var operation in operations)
        {
            if (operation.ValueKind == JsonValueKind.Undefined)
            {
                return ToolResult.Fail(EngineErrors.InvalidArguments, "Undefined patch operation");
            }
            bytes += Encoding.UTF8.GetByteCount(operation.GetRawText());
            if (bytes > PatchLimits.MaxJsonBytes)
            {
                return ToolResult.Fail(EngineErrors.ResourceLimit, "Patch exceeds the UTF-8 input limit");
            }
        }
        // JsonElement retains duplicate members and numeric spelling for the shared parser.
        var parsed = PatchOperationParser.Parse("[" + string.Join(',', operations.Select(operation => operation.GetRawText())) + "]");
        if (!parsed.Success)
        {
            return PatchFailure(parsed.Error!);
        }

        var (confinedOperations, confinementError) = ConfinePatchImageSources(parsed.Operations!);
        if (confinementError is not null)
        {
            return confinementError;
        }

        var (handle, error) = OpenConfined(idOrPath, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (handle is null)
        {
            return DocumentNotFound(idOrPath);
        }

        try
        {
            EnsureConfinedSource(handle);
            var result = _engine.ApplyPatch(handle, confinedOperations!, expectedRevn, cancellationToken);
            if (!result.Success)
            {
                return PatchFailure(result.Error!);
            }

            return ToolResult.Success(new
            {
                revn = result.Revn,
                applied = result.Applied,
                createdIds = result.CreatedIds.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value),
                warnings = result.Warnings.Select(w => new { w.Code, w.Message, w.Path }).ToArray()
            });
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return ToolResult.FromException(ex, "PATCH_FAILED");
        }
    }

    private static CallToolResult PatchFailure(PatchError error) =>
        ToolResult.Fail(error.Code, error.Message, error.Path, error.OperationIndex);

    /// <summary>
    /// Apply the same image-source confinement as <see cref="AddNode"/> to node JSON inside a
    /// patch: relative and file paths must stay within the working directory and are
    /// normalized to relative form; absolute non-file URLs pass through.
    /// </summary>
    private (IReadOnlyList<PatchOperation>? operations, CallToolResult? error) ConfinePatchImageSources(
        IReadOnlyList<PatchOperation> operations)
    {
        var result = new List<PatchOperation>(operations.Count);
        for (var index = 0; index < operations.Count; index++)
        {
            var operation = operations[index];
            string? error = null;
            switch (operation)
            {
                case SetOperation { Path: "/src", Value.ValueKind: JsonValueKind.String } set:
                    if (TryConfineImageSource(set.Value.GetString(), out var normalized, out error))
                    {
                        operation = set with { Value = JsonSerializer.SerializeToElement(normalized) };
                    }
                    break;

                case SetNodeOperation setNode:
                    {
                        var node = setNode.Node.DeepClone().AsObject();
                        if (TryConfineImageSourcesInNode(node, out error))
                        {
                            operation = setNode with { Node = node };
                        }
                        break;
                    }

                case AddNodeOperation addNode:
                    {
                        var node = addNode.Node.DeepClone().AsObject();
                        if (TryConfineImageSourcesInNode(node, out error))
                        {
                            operation = addNode with { Node = node };
                        }
                        break;
                    }
            }

            if (error is not null)
            {
                return (null, ToolResult.Fail(EngineErrors.PathEscape, $"Operation {index} ({operation.Op}): {error}", null, index));
            }

            result.Add(operation);
        }

        return (result, null);
    }

    private bool TryConfineImageSourcesInNode(JsonObject node, out string? error)
    {
        error = null;
        if (node["type"] is JsonValue typeValue && typeValue.TryGetValue<string>(out var type) && type == "image" &&
            node["src"] is JsonValue srcValue && srcValue.TryGetValue<string>(out var src))
        {
            if (!TryConfineImageSource(src, out var normalized, out error))
            {
                return false;
            }

            node["src"] = normalized;
        }

        if (node["children"] is JsonArray children)
        {
            foreach (var child in children)
            {
                if (child is JsonObject childObject && !TryConfineImageSourcesInNode(childObject, out error))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private void EnsureConfinedSource(DocumentHandle handle)
    {
        if (handle.SourcePath is null)
        {
            return;
        }

        var source = _pathConfinement.Confine(handle.SourcePath);
        if (!source.Success)
        {
            throw new DocumentEngineException(EngineErrors.PathEscape, source.Error!);
        }
    }

    [McpServerTool(Title = "Export Document", ReadOnly = false)]
    [Description("Export a document to SVG, PNG, JPEG, or WebP format")]
    public async Task<CallToolResult> ExportDocument(
        [Description("Document ID (GUID) or file path within working directory")] string idOrPath,
        [Description("Output file path within working directory")] string outputPath,
        [Description("Export format: svg, png, jpeg, webp (default: svg)")] string format = "svg",
        [Description("Uniform geometry and viewport scale (default: 1.0); use 0.25 for a 128-unit canvas at 32px")] double scale = 1.0,
        [Description("Logical viewport width before scale; crops/pads, does not fit geometry (standalone SVG or raster)")] int? width = null,
        [Description("Logical viewport height before scale; crops/pads, does not fit geometry (standalone SVG or raster)")] int? height = null,
        [Description("Background color in hex format (raster formats only)")] string? background = null,
        [Description("Validate before export and reject errors or warnings")] bool strict = false,
        [Description("Additional validation profile applied before export: web-icon")] string? profile = null,
        [Description("Emit a reusable SVG symbol fragment with this id for sprite assembly (svg only; excludes scale/width/height)")] string? symbolId = null,
        [Description("Export only the subtree rooted at this node id (GUID), cropped to its own bounds and drawn in isolation; ancestor transforms and clipping are not applied (excludes symbolId/width/height)")] string? nodeId = null,
        [Description("Write currentColor for node fills and strokes that resolve to this color (svg only)")] string? currentColor = null,
        [Description("Comma-separated directories within the working directory whose .ttf/.otf files are searched before the bundled Inter family and system fonts (raster only)")] string? fontDirs = null,
        [Description("Allow host-installed fonts for families that the font directories and bundled Inter do not provide (default: true; raster only)")] bool systemFonts = true,
        [Description("Fail with FONT_NOT_FOUND when a font family cannot be resolved instead of substituting bundled Inter (raster only)")] bool strictFonts = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ValidationProfiles.TryParse(profile, out var validationProfile))
        {
            return ToolResult.Fail(EngineErrors.InvalidArguments,
                $"Unknown validation profile. Supported: {ValidationProfiles.WebIcon}");
        }

        var formatLower = format.ToLowerInvariant();
        if (formatLower is not ("svg" or "png" or "jpeg" or "jpg" or "webp"))
        {
            return ToolResult.Fail(EngineErrors.ExportUnsupported, $"Unsupported export format: {format}");
        }
        if (!double.IsFinite(scale) || scale is < 0.1 or > 4.0 ||
            width is < 1 or > MaxRasterDimension || height is < 1 or > MaxRasterDimension)
        {
            return ToolResult.Fail(EngineErrors.InvalidProperty,
                $"Scale must be 0.1-4 and dimensions must be 1-{MaxRasterDimension}");
        }

        if (symbolId is not null)
        {
            if (formatLower is not "svg")
            {
                return ToolResult.Fail(EngineErrors.InvalidArguments, "symbolId only applies to svg export");
            }
            if (scale != 1.0 || width is not null || height is not null)
            {
                return ToolResult.Fail(EngineErrors.InvalidArguments,
                    "symbolId cannot be combined with scale, width, or height; a symbol has no intrinsic size");
            }
            if (!SvgExportAdapter.IsValidSymbolId(symbolId))
            {
                return ToolResult.Fail(EngineErrors.InvalidProperty,
                    $"Symbol id must start with a letter, use only letters, digits, '-' or '_', and be at most {SvgExportAdapter.MaxSymbolIdCharacters} characters");
            }
        }

        Guid? subtreeNodeId = null;
        if (nodeId is not null)
        {
            if (symbolId is not null || width is not null || height is not null)
            {
                return ToolResult.Fail(EngineErrors.InvalidArguments,
                    "nodeId cannot be combined with symbolId, width, or height; the exported size is intrinsic to the subtree");
            }
            if (!Guid.TryParse(nodeId, out var parsedNodeId))
            {
                return ToolResult.Fail(EngineErrors.InvalidArguments, $"nodeId must be a valid node id (GUID): {nodeId}");
            }
            subtreeNodeId = parsedNodeId;
        }

        if (currentColor is not null)
        {
            if (formatLower is not "svg")
            {
                return ToolResult.Fail(EngineErrors.InvalidArguments, "currentColor only applies to svg export");
            }
            if (TooLong(currentColor, MaxPersistedValueLength) || !DocumentLimits.IsValidColor(currentColor))
            {
                return ToolResult.Fail(EngineErrors.InvalidProperty,
                    "currentColor must be a hex, rgb(), or rgba() color");
            }
        }

        var (fontDirectories, fontError) = ConfineFontDirectories(fontDirs);
        if (fontError is not null)
        {
            return fontError;
        }
        if (!IsRasterFormat(formatLower) && (fontDirectories.Count > 0 || !systemFonts || strictFonts))
        {
            return ToolResult.Fail(EngineErrors.InvalidArguments,
                "fontDirs, systemFonts, and strictFonts only apply to raster formats (png, jpeg, webp)");
        }

        var (handle, error) = OpenConfined(idOrPath, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (handle is null)
        {
            return DocumentNotFound(idOrPath);
        }

        if (strict || validationProfile != ValidationProfile.Default)
        {
            var validation = _engine.Validate(handle.Document, new ValidationOptions
            {
                Profile = validationProfile,
                BackgroundOverride = background
            });
            var validationError = validation.Errors.FirstOrDefault();
            if (validationError is not null)
            {
                return ToolResult.Fail(validationError.Code, validationError.Message, validationError.Path);
            }

            var validationWarning = strict ? validation.Warnings.FirstOrDefault() : null;
            if (validationWarning is not null)
            {
                return ToolResult.Fail(validationWarning.Code, validationWarning.Message, validationWarning.Path);
            }
        }

        // A sub-node export sizes the surface to the subtree bounds, not the canvas, so the
        // canvas-based pre-check does not apply; the renderer's own dimension guard covers it.
        if (IsRasterFormat(formatLower) && subtreeNodeId is null)
        {
            var effectiveWidth = width ?? Math.Ceiling(handle.Document.Canvas.Width * scale);
            var effectiveHeight = height ?? Math.Ceiling(handle.Document.Canvas.Height * scale);
            if (effectiveWidth > MaxRasterDimension || effectiveHeight > MaxRasterDimension)
            {
                return ToolResult.Fail(EngineErrors.InvalidDimensions,
                    $"Effective raster dimensions cannot exceed {MaxRasterDimension}");
            }
        }

        var outputResult = _pathConfinement.Confine(outputPath);
        if (!outputResult.Success)
        {
            return ToolResult.Fail(EngineErrors.PathEscape, outputResult.Error!);
        }

        var confinedOutputPath = outputResult.Path!;
        try
        {
            byte[] data;
            string mimeType;
            int? resultWidth = null;
            int? resultHeight = null;
            IReadOnlyList<RenderDiagnostic> diagnostics = [];

            if (IsRasterFormat(formatLower))
            {
                // Explicit names only: the internal Rgba8 buffer is never a file format.
                var renderFormat = formatLower switch
                {
                    "png" => RenderFormat.Png,
                    "jpeg" or "jpg" => RenderFormat.Jpeg,
                    "webp" => RenderFormat.Webp,
                    _ => throw new DocumentEngineException(EngineErrors.ExportUnsupported, $"Unsupported export format: {format}")
                };

                var renderOptions = new RenderOptions
                {
                    Scale = scale,
                    Width = width,
                    Height = height,
                    Background = background,
                    Format = renderFormat,
                    FontDirectories = fontDirectories,
                    SystemFonts = systemFonts,
                    StrictFonts = strictFonts
                };

                var renderResult = subtreeNodeId is { } rasterNode
                    ? _engine.RenderNode(handle.Document, rasterNode, renderOptions, cancellationToken)
                    : _renderer.Render(handle.Document, renderOptions, cancellationToken);
                data = renderResult.Data;
                mimeType = renderResult.MimeType;
                resultWidth = renderResult.Width;
                resultHeight = renderResult.Height;
                diagnostics = renderResult.Diagnostics;
            }
            else
            {
                var exportOptions = new ExportOptions
                {
                    Format = formatLower,
                    Scale = scale,
                    Width = width,
                    Height = height,
                    Background = background,
                    SymbolId = symbolId,
                    CurrentColor = currentColor
                };
                var exportResult = subtreeNodeId is { } vectorNode
                    ? await _engine.ExportNodeAsync(handle.Document, vectorNode, exportOptions, cancellationToken)
                    : await _engine.ExportAsync(handle.Document, exportOptions, cancellationToken);
                data = exportResult.Data;
                mimeType = exportResult.MimeType;
                diagnostics = exportResult.Diagnostics;
            }

            if (data.LongLength > MaxOutputBytes)
            {
                return ToolResult.Fail("OUTPUT_TOO_LARGE", $"Output exceeds the {MaxOutputBytes} byte limit");
            }

            var outputDir = Path.GetDirectoryName(confinedOutputPath);
            if (!string.IsNullOrEmpty(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            var recheckedOutput = _pathConfinement.Confine(confinedOutputPath);
            if (!recheckedOutput.Success)
            {
                return ToolResult.Fail(EngineErrors.PathEscape, recheckedOutput.Error!);
            }
            await WriteAllBytesAtomicallyAsync(recheckedOutput.Path!, data, cancellationToken);

            var result = new Dictionary<string, object>
            {
                ["path"] = _pathConfinement.RelativePath(recheckedOutput.Path!),
                ["format"] = formatLower,
                ["mimeType"] = mimeType,
                ["size"] = data.Length,
                ["diagnostics"] = ToDiagnosticPayload(diagnostics)
            };

            if (resultWidth.HasValue) result["width"] = resultWidth.Value;
            if (resultHeight.HasValue) result["height"] = resultHeight.Value;

            return ToolResult.Success(result);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return ToolResult.FromException(ex, "EXPORT_FAILED");
        }
    }

    public Task<CallToolResult> ExportDocument(
        string idOrPath,
        string outputPath,
        string format,
        double scale,
        int? width,
        int? height,
        string? background,
        bool strict,
        CancellationToken cancellationToken) =>
        ExportDocument(
            idOrPath,
            outputPath,
            format,
            scale,
            width,
            height,
            background,
            strict,
            profile: null,
            cancellationToken: cancellationToken);

    private static bool IsRasterFormat(string format) =>
        format is "png" or "jpeg" or "jpg" or "webp";

    /// <summary>
    /// Splits a comma-separated font directory list and confines every entry to the
    /// working directory. Entries must already exist as directories.
    /// </summary>
    private (IReadOnlyList<string> Directories, CallToolResult? Error) ConfineFontDirectories(string? fontDirs)
    {
        if (string.IsNullOrWhiteSpace(fontDirs))
        {
            return ([], null);
        }

        var entries = fontDirs.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (entries.Length > FontResolutionOptions.MaxFontDirectories)
        {
            return ([], ToolResult.Fail(EngineErrors.InvalidArguments,
                $"At most {FontResolutionOptions.MaxFontDirectories} font directories are allowed"));
        }

        // Confine every entry before touching the filesystem so an escaping path is
        // always reported as PATH_ESCAPE, whatever its position in the list.
        var directories = new List<string>(entries.Length);
        foreach (var entry in entries)
        {
            var confined = _pathConfinement.Confine(entry);
            if (!confined.Success)
            {
                return ([], ToolResult.Fail(EngineErrors.PathEscape, confined.Error!));
            }
            directories.Add(confined.Path!);
        }

        for (var index = 0; index < directories.Count; index++)
        {
            if (!Directory.Exists(directories[index]))
            {
                return ([], ToolResult.Fail(EngineErrors.InvalidArguments, $"Font directory not found: {entries[index]}"));
            }
        }

        return (directories, null);
    }

    /// <summary>
    /// Builds the measurement <see cref="FontPolicy"/> for bounds/lint from confined font options,
    /// or null for the default policy so the engine reuses its shared default measurer.
    /// </summary>
    private static FontPolicy? BuildFontPolicy(IReadOnlyList<string> directories, bool systemFonts, bool strictFonts) =>
        directories.Count == 0 && systemFonts && !strictFonts
            ? null
            : new FontPolicy { FontDirectories = directories, SystemFonts = systemFonts, StrictFonts = strictFonts };

    private static object[] ToDiagnosticPayload(IReadOnlyList<RenderDiagnostic> diagnostics) =>
        diagnostics.Select(diagnostic => (object)new { code = diagnostic.Code, message = diagnostic.Message }).ToArray();

    [McpServerTool(Title = "Get Document JSON", ReadOnly = true)]
    [Description("Get canonical JSON for a document or one node subtree; select nodeId to avoid returning unrelated content")]
    public CallToolResult GetDocumentJson(
        [Description("Document ID (GUID) or file path within working directory")] string idOrPath,
        [Description("Optional node ID (GUID) whose subtree is returned instead of the full document")] string? nodeId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (handle, error) = OpenConfined(idOrPath, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (handle is null)
        {
            return DocumentNotFound(idOrPath);
        }

        if (nodeId is null)
        {
            return ToolResult.Success(new { json = _engine.ToJson(handle.Document), revn = handle.Document.Revn });
        }
        if (!Guid.TryParse(nodeId, out var id) || _engine.FindNode(handle.Document, id) is not { } node)
        {
            return ToolResult.Fail(EngineErrors.NodeNotFound, "Subtree node was not found");
        }
        return ToolResult.Success(new { json = DocumentSerializer.SerializeSubtree(node), nodeId = id, revn = handle.Document.Revn });
    }

    [McpServerTool(Title = "Render Preview", ReadOnly = true)]
    [Description("Render a document to a PNG image and return it as an inline image block. EXPERIMENTAL.")]
    public CallToolResult RenderPreview(
        [Description("Document ID (GUID) or file path within working directory")] string idOrPath,
        [Description("Explicit scale factor (0.1-4); omitted with no dimensions fits the canvas into a 512px thumbnail without upscaling")] double? scale = null,
        [Description("Logical viewport width before scale (max: 2048); crops/pads, does not fit geometry or use the automatic thumbnail scale")] int? width = null,
        [Description("Logical viewport height before scale (max: 2048); crops/pads, does not fit geometry or use the automatic thumbnail scale")] int? height = null,
        [Description("Comma-separated directories within the working directory whose .ttf/.otf files are searched before the bundled Inter family and system fonts")] string? fontDirs = null,
        [Description("Allow host-installed fonts for families that the font directories and bundled Inter do not provide (default: true)")] bool systemFonts = true,
        [Description("Fail with FONT_NOT_FOUND when a font family cannot be resolved instead of substituting bundled Inter")] bool strictFonts = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (scale is { } requestedScale && (!double.IsFinite(requestedScale) || requestedScale is < 0.1 or > 4.0) ||
            width is < 1 or > MaxRasterDimension || height is < 1 or > MaxRasterDimension)
        {
            return ToolResult.Fail(EngineErrors.InvalidProperty,
                $"Scale must be 0.1-4 and dimensions must be 1-{MaxRasterDimension}");
        }

        var (fontDirectories, fontError) = ConfineFontDirectories(fontDirs);
        if (fontError is not null)
        {
            return fontError;
        }

        var (handle, error) = OpenConfined(idOrPath, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (handle is null)
        {
            return DocumentNotFound(idOrPath);
        }

        var thumbnail = scale is null && width is null && height is null;
        if (!double.IsFinite(handle.Document.Canvas.Width) || !double.IsFinite(handle.Document.Canvas.Height) ||
            handle.Document.Canvas.Width <= 0 || handle.Document.Canvas.Height <= 0 ||
            handle.Document.Canvas.Width > 100_000 || handle.Document.Canvas.Height > 100_000)
        {
            return ToolResult.Fail(EngineErrors.InvalidDimensions, "Canvas dimensions must be finite, positive and at most 100000");
        }
        var effectiveScale = scale ?? (thumbnail
            ? Math.Min(1, 512 / Math.Max(handle.Document.Canvas.Width, handle.Document.Canvas.Height))
            : 1);
        // A very thin canvas can scale below half a pixel. Pad the read-only viewport
        // to one output pixel without changing the source or its uniform scale.
        if (thumbnail)
        {
            if (handle.Document.Canvas.Width * effectiveScale < 0.5)
                width = checked((int)Math.Ceiling(1 / effectiveScale));
            if (handle.Document.Canvas.Height * effectiveScale < 0.5)
                height = checked((int)Math.Ceiling(1 / effectiveScale));
        }
        var effectiveWidth = Math.Ceiling((width ?? handle.Document.Canvas.Width) * effectiveScale);
        var effectiveHeight = Math.Ceiling((height ?? handle.Document.Canvas.Height) * effectiveScale);
        if (effectiveWidth > MaxRasterDimension || effectiveHeight > MaxRasterDimension)
        {
            return ToolResult.Fail(EngineErrors.InvalidDimensions,
                $"Effective preview dimensions cannot exceed {MaxRasterDimension}");
        }

        try
        {
            var options = new RenderOptions
            {
                Scale = effectiveScale,
                Width = width,
                Height = height,
                Format = RenderFormat.Png,
                FontDirectories = fontDirectories,
                SystemFonts = systemFonts,
                StrictFonts = strictFonts
            };

            var result = _renderer.Render(handle.Document, options, cancellationToken);
            if (result.Data.LongLength > MaxOutputBytes)
            {
                return ToolResult.Fail("OUTPUT_TOO_LARGE", "Preview exceeds the output limit");
            }

            var response = ToolResult.Success(new
            {
                width = result.Width,
                height = result.Height,
                size = result.Data.Length,
                mimeType = result.MimeType,
                diagnostics = ToDiagnosticPayload(result.Diagnostics)
            });
            response.Content.Insert(0, ImageContentBlock.FromBytes(result.Data, result.MimeType));
            return response;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return ToolResult.FromException(ex, "RENDER_FAILED");
        }
    }

    // ---- Design feedback: lint and contact sheet ----

    [McpServerTool(Title = "Lint Document")]
    [Description("Run advisory design lints (LINT001 text contrast, LINT002 hairline strokes, LINT003 off-grid icon geometry, LINT004 content outside the canvas, LINT005 unused tokens, LINT006 invisible content, LINT007 symbol readiness) and return the findings. Lint never blocks export.")]
    public CallToolResult LintDocument(
        [Description("Document ID (GUID) or file path within working directory")] string idOrPath,
        [Description("Lint profile: web-icon (forces the grid rule and enables symbol readiness)")] string? profile = null,
        [Description("Pixel size the document will be shown at along its larger edge; scales the hairline rule (default: native size)")] double? targetSize = null,
        [Description("Off-grid rule requires whole pixels (warning) instead of half pixels (info)")] bool strictGrid = false,
        [Description("Comma-separated directories within the working directory whose .ttf/.otf files are searched before the bundled Inter family and system fonts when measuring text")] string? fontDirs = null,
        [Description("Allow host-installed fonts for families that the font directories and bundled Inter do not provide (default: true)")] bool systemFonts = true,
        [Description("Treat an unresolved font family as a hard failure instead of substituting bundled Inter")] bool strictFonts = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ValidationProfiles.TryParse(profile, out var lintProfile))
        {
            return ToolResult.Fail(EngineErrors.InvalidArguments,
                $"Unknown lint profile. Supported: {ValidationProfiles.WebIcon}");
        }
        if (targetSize is { } requested && (!double.IsFinite(requested) || requested <= 0))
        {
            return ToolResult.Fail(EngineErrors.InvalidProperty, "targetSize must be a positive number of pixels");
        }

        var (fontDirectories, fontError) = ConfineFontDirectories(fontDirs);
        if (fontError is not null)
        {
            return fontError;
        }

        var (handle, error) = OpenConfined(idOrPath, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (handle is null)
        {
            return DocumentNotFound(idOrPath);
        }

        try
        {
            var result = _engine.Lint(handle.Document, new LintOptions
            {
                Profile = lintProfile,
                TargetSize = targetSize,
                StrictGrid = strictGrid
            }, BuildFontPolicy(fontDirectories, systemFonts, strictFonts), cancellationToken);
            return ToolResult.Success(LintPayload.Result(handle, result));
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return ToolResult.FromException(ex, "LINT_FAILED");
        }
    }

    [McpServerTool(Title = "Render Contact Sheet")]
    [Description("Render several documents into one PNG grid (each aspect-fitted into a square cell, optionally labelled with its name) and return it as an inline image block. EXPERIMENTAL.")]
    public CallToolResult RenderContactSheet(
        [Description("Document file paths within the working directory: comma-separated, or a JSON array string (max 256)")] string paths,
        [Description("Grid columns (default: ceil(sqrt(count)), max: 256)")] int? columns = null,
        [Description("Square cell size in pixels (default: 128, range 8-2048)")] int cell = ContactSheetOptions.DefaultCellSize,
        [Description("Print each document's name under its cell")] bool labels = false,
        [Description("Comma-separated directories within the working directory whose .ttf/.otf files are searched before the bundled Inter family and system fonts for every tile and label")] string? fontDirs = null,
        [Description("Allow host-installed fonts for families that the font directories and bundled Inter do not provide (default: true)")] bool systemFonts = true,
        [Description("Fail a tile with FONT_NOT_FOUND when a font family cannot be resolved instead of substituting bundled Inter")] bool strictFonts = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!PathList.TryParse(paths, ContactSheetLayout.MaxEntries, out var pathList, out var pathError))
        {
            return ToolResult.Fail(EngineErrors.InvalidArguments, pathError!);
        }
        if (cell < ContactSheetOptions.MinCellSize || cell > MaxRasterDimension)
        {
            return ToolResult.Fail(EngineErrors.InvalidProperty,
                $"Cell size must be between {ContactSheetOptions.MinCellSize} and {MaxRasterDimension}");
        }
        if (columns is < 1 or > ContactSheetLayout.MaxColumns)
        {
            return ToolResult.Fail(EngineErrors.InvalidProperty,
                $"Columns must be between 1 and {ContactSheetLayout.MaxColumns}");
        }

        var (fontDirectories, fontError) = ConfineFontDirectories(fontDirs);
        if (fontError is not null)
        {
            return fontError;
        }

        var options = new ContactSheetOptions
        {
            Columns = columns,
            CellSize = cell,
            Labels = labels,
            FontDirectories = fontDirectories,
            SystemFonts = systemFonts,
            StrictFonts = strictFonts
        };

        ContactSheetLayout layout;
        try
        {
            layout = ContactSheetLayout.Compute(pathList.Count, options);
        }
        catch (DocumentEngineException ex)
        {
            return ToolResult.Fail(ex.Code, ex.Message);
        }

        if (layout.Width > MaxRasterDimension || layout.Height > MaxRasterDimension)
        {
            return ToolResult.Fail(EngineErrors.InvalidDimensions,
                $"Effective contact sheet dimensions cannot exceed {MaxRasterDimension}; got {layout.Width}x{layout.Height}");
        }

        var entries = new List<ContactSheetEntry>(pathList.Count);
        var sources = new List<string>(pathList.Count);
        foreach (var path in pathList)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var confined = _pathConfinement.Confine(path);
            if (!confined.Success)
            {
                return ToolResult.Fail(EngineErrors.PathEscape, $"{path}: {confined.Error}");
            }
            if (!File.Exists(confined.Path!))
            {
                return DocumentNotFound(path);
            }
            if (new FileInfo(confined.Path!).Length > MaxInputFileBytes)
            {
                return ToolResult.Fail("INPUT_TOO_LARGE", $"{path}: document exceeds the {MaxInputFileBytes} byte limit");
            }

            try
            {
                var handle = _engine.Open(confined.Path!, cancellationToken);
                if (handle is null)
                {
                    return DocumentNotFound(path);
                }

                entries.Add(new ContactSheetEntry(handle.Document, handle.Document.Name));
                sources.Add(_pathConfinement.RelativePath(confined.Path!));
            }
            catch (Exception ex) when (IsExpected(ex))
            {
                return ToolResult.FromException(ex, "OPEN_FAILED");
            }
        }

        try
        {
            var result = _engine.RenderContactSheet(entries, options, cancellationToken);
            if (result.Data.LongLength > MaxOutputBytes)
            {
                return ToolResult.Fail("OUTPUT_TOO_LARGE", "Contact sheet exceeds the output limit");
            }

            var response = ToolResult.Success(new
            {
                width = result.Width,
                height = result.Height,
                columns = result.Layout.Columns,
                rows = result.Layout.Rows,
                cellSize = result.Layout.CellSize,
                gap = result.Layout.Gap,
                labels,
                size = result.Data.Length,
                mimeType = result.MimeType,
                cells = result.Cells.Select((sheetCell, index) => new
                {
                    index = sheetCell.Index,
                    label = sheetCell.Label,
                    source = sources[index],
                    column = sheetCell.Column,
                    row = sheetCell.Row,
                    cell = new { x = sheetCell.Cell.X, y = sheetCell.Cell.Y, width = sheetCell.Cell.Width, height = sheetCell.Cell.Height },
                    image = new { x = sheetCell.Image.X, y = sheetCell.Image.Y, width = sheetCell.Image.Width, height = sheetCell.Image.Height }
                })
            });
            response.Content.Insert(0, ImageContentBlock.FromBytes(result.Data, result.MimeType));
            return response;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return ToolResult.FromException(ex, "RENDER_FAILED");
        }
    }

    [McpServerTool(Title = "Import SVG", ReadOnly = false)]
    [Description("Import an SVG file into an AGD document. Best-effort conversion with warnings for unsupported features.")]
    public async Task<CallToolResult> ImportSvg(
        [Description("Path to SVG file within working directory")] string svgPath,
        [Description("Output .agd.json file path within working directory (default: derived from input)")] string? outputPath = null,
        [Description("Document name (default: derived from filename)")] string? documentName = null,
        [Description("Overwrite the output file if it exists")] bool force = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (documentName is { Length: > MaxNameLength })
        {
            return ToolResult.Fail(EngineErrors.InvalidProperty,
                $"Document name cannot exceed {MaxNameLength} characters");
        }

        var svgResult = _pathConfinement.Confine(svgPath);
        if (!svgResult.Success)
        {
            return ToolResult.Fail(EngineErrors.PathEscape, svgResult.Error!);
        }

        var confinedSvgPath = svgResult.Path!;

        if (!File.Exists(confinedSvgPath))
        {
            return ToolResult.Fail("FILE_NOT_FOUND", $"SVG file not found: {svgPath}");
        }

        if (new FileInfo(confinedSvgPath).Length > MaxInputFileBytes)
        {
            return ToolResult.Fail("INPUT_TOO_LARGE", $"SVG input exceeds the {MaxInputFileBytes} byte limit");
        }

        var extension = Path.GetExtension(confinedSvgPath).ToLowerInvariant();
        if (extension != ".svg")
        {
            return ToolResult.Fail("UNSUPPORTED_FORMAT", $"Only SVG import is supported; got: {extension}");
        }

        var options = new SvgImportOptions { DocumentName = documentName };
        var svg = await File.ReadAllTextAsync(confinedSvgPath, cancellationToken);
        var importResult = _engine.ImportSvg(svg, options, cancellationToken);

        if (!importResult.Success || importResult.Document is null)
        {
            return ToolResult.Fail(importResult.ErrorCode ?? "IMPORT_FAILED", importResult.ErrorMessage ?? "Import failed");
        }

        var document = importResult.Document;

        var derivedOutputPath = outputPath ?? DeriveOutputPath(confinedSvgPath);
        var outputResult = _pathConfinement.Confine(derivedOutputPath);
        if (!outputResult.Success)
        {
            return ToolResult.Fail(EngineErrors.PathEscape, outputResult.Error!);
        }

        var confinedOutputPath = outputResult.Path!;

        try
        {
            SaveCreatedDocument(document, confinedOutputPath, force, cancellationToken);

            return ToolResult.Success(new
            {
                locator = _pathConfinement.RelativePath(confinedOutputPath),
                documentId = document.Id,
                documentName = document.Name,
                canvas = new
                {
                    width = document.Canvas.Width,
                    height = document.Canvas.Height
                },
                nodeCount = CountNodesInDocument(document),
                warnings = importResult.Warnings.Select(w => new
                {
                    code = w.Code.ToString(),
                    message = w.Message,
                    context = w.Context
                }).ToArray()
            });
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return ToolResult.FromException(ex, "IMPORT_FAILED");
        }
    }

    private static string DeriveOutputPath(string inputPath)
    {
        var dir = Path.GetDirectoryName(inputPath) ?? ".";
        var name = Path.GetFileNameWithoutExtension(inputPath);
        return Path.Combine(dir, $"{name}.agd.json");
    }

    private static int CountNodesInDocument(Document document)
    {
        int count = 0;
        CountNodes(document.Root, ref count);
        return count;
    }

    private static void CountNodes(Node node, ref int count)
    {
        count++;
        if (node is IContainerNode container)
        {
            foreach (var child in container.Children)
            {
                CountNodes(child, ref count);
            }
        }
    }

    /// <summary>
    /// Resolve every locator, including GUID-backed repository files, through confinement.
    /// </summary>
    private (DocumentHandle? handle, CallToolResult? error) OpenConfined(
        string idOrPath,
        CancellationToken cancellationToken)
    {
        var isGuid = Guid.TryParse(idOrPath, out var documentId);
        var pathToConfine = isGuid ? $"{documentId:D}.agd.json" : idOrPath;

        var result = _pathConfinement.Confine(pathToConfine);
        if (!result.Success)
        {
            return (null, ToolResult.Fail(EngineErrors.PathEscape, result.Error!));
        }

        if (File.Exists(result.Path!) && new FileInfo(result.Path!).Length > MaxInputFileBytes)
        {
            return (null, ToolResult.Fail("INPUT_TOO_LARGE",
                $"Document exceeds the {MaxInputFileBytes} byte limit"));
        }

        try
        {
            var handle = _engine.Open(isGuid ? documentId.ToString("D") : result.Path!, cancellationToken);
            if (handle?.SourcePath is not null && File.Exists(handle.SourcePath) &&
                new FileInfo(handle.SourcePath).Length > MaxInputFileBytes)
            {
                return (null, ToolResult.Fail("INPUT_TOO_LARGE",
                    $"Document exceeds the {MaxInputFileBytes} byte limit"));
            }
            return (handle, null);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return (null, ToolResult.FromException(ex, "OPEN_FAILED"));
        }
    }

    private static CallToolResult DocumentNotFound(string idOrPath) =>
        ToolResult.Fail(EngineErrors.DocumentNotFound, $"Document not found: {idOrPath}");

    // ---- Path operations (Clipper2) ----

    [McpServerTool(Title = "Path Boolean Operation")]
    [Description("Perform a boolean operation (union, intersect, difference, xor) on two SVG paths")]
    public CallToolResult PathBoolean(
        [Description("Boolean operation: union, intersect, difference, xor")] string operation,
        [Description("Subject path (SVG path d attribute)")] string subjectPath,
        [Description("Clip path (SVG path d attribute)")] string clipPath,
        [Description("Fill rule for overlapping contours: nonzero, evenodd (default: nonzero)")] string fillRule = "nonzero",
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (subjectPath.Length > MaxTextLength || clipPath.Length > MaxTextLength)
        {
            return ToolResult.Fail("INPUT_TOO_LARGE", "Path input exceeds the allowed limit");
        }

        if (!TryParseBooleanOp(operation, out var op))
        {
            return ToolResult.Fail("INVALID_OPERATION",
                $"Unknown operation: '{operation}'. Supported: union, intersect, difference, xor");
        }

        if (!PathFillRules.TryParse(fillRule, out var rule))
        {
            return ToolResult.Fail("INVALID_FILL_RULE",
                $"Unknown fill rule: '{fillRule}'. Supported: nonzero, evenodd");
        }

        var result = _engine.PathBooleanOp(subjectPath, clipPath, op, rule, cancellationToken);

        if (!result.Success)
        {
            return ToolResult.Fail("PATH_OP_FAILED", result.Error!);
        }

        return ToolResult.Success(new
        {
            operation = operation.ToLowerInvariant(),
            fillRule = PathFillRules.ToName(rule),
            pathData = result.PathData
        });
    }

    [McpServerTool(Title = "Path Stroke Offset")]
    [Description("Offset a path to create a stroke outline (positive = outward, negative = inward)")]
    public CallToolResult PathOffset(
        [Description("Input path (SVG path d attribute)")] string path,
        [Description("Offset distance (positive = outward, negative = inward)")] double offset,
        [Description("Join type: miter, round, square (default: round)")] string join = "round",
        [Description("End type: polygon, joined, butt, square, round (default: polygon)")] string end = "polygon",
        [Description("Miter limit for miter joins (default: 2.0)")] double miterLimit = 2.0,
        [Description("Fill rule for overlapping contours: nonzero, evenodd (default: nonzero; evenodd resolves overlaps into holes before offsetting)")] string fillRule = "nonzero",
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (path.Length > MaxTextLength || !double.IsFinite(offset) ||
            !double.IsFinite(miterLimit) || miterLimit is <= 0 or > 1000)
        {
            return ToolResult.Fail(EngineErrors.InvalidProperty,
                "Path input or numeric offset parameters are outside the allowed range");
        }

        if (!TryParseJoinType(join, out var joinType))
        {
            return ToolResult.Fail("INVALID_JOIN",
                $"Unknown join type: '{join}'. Supported: miter, round, square");
        }

        if (!TryParseEndType(end, out var endType))
        {
            return ToolResult.Fail("INVALID_END",
                $"Unknown end type: '{end}'. Supported: polygon, joined, butt, square, round");
        }

        if (!PathFillRules.TryParse(fillRule, out var rule))
        {
            return ToolResult.Fail("INVALID_FILL_RULE",
                $"Unknown fill rule: '{fillRule}'. Supported: nonzero, evenodd");
        }

        var result = _engine.PathStrokeOffset(
            path,
            offset,
            joinType,
            endType,
            miterLimit,
            rule,
            cancellationToken);

        if (!result.Success)
        {
            return ToolResult.Fail("PATH_OP_FAILED", result.Error!);
        }

        return ToolResult.Success(new
        {
            offset,
            join = join.ToLowerInvariant(),
            end = end.ToLowerInvariant(),
            fillRule = PathFillRules.ToName(rule),
            pathData = result.PathData
        });
    }

    [McpServerTool(Title = "Path Union All")]
    [Description("Union multiple SVG paths into a single path")]
    public CallToolResult PathUnionAll(
        [Description("Array of SVG path d attributes to union")] string[] paths,
        [Description("Fill rule for overlapping contours: nonzero, evenodd (default: nonzero)")] string fillRule = "nonzero",
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (paths.Length == 0)
        {
            return ToolResult.Fail("NO_PATHS", "No paths provided");
        }

        if (paths.Length > 256 || paths.Sum(path => (long)path.Length) > MaxInputFileBytes)
        {
            return ToolResult.Fail("INPUT_TOO_LARGE", "Too many paths or total path input is too large");
        }

        if (!PathFillRules.TryParse(fillRule, out var rule))
        {
            return ToolResult.Fail("INVALID_FILL_RULE",
                $"Unknown fill rule: '{fillRule}'. Supported: nonzero, evenodd");
        }

        var result = _engine.PathUnionAll(paths, rule, cancellationToken);

        if (!result.Success)
        {
            return ToolResult.Fail("PATH_OP_FAILED", result.Error!);
        }

        return ToolResult.Success(new
        {
            inputCount = paths.Length,
            fillRule = PathFillRules.ToName(rule),
            pathData = result.PathData
        });
    }

    // ---- Design Tokens ----

    [McpServerTool(Title = "Set Color Token")]
    [Description("Add or update a color token in a document")]
    public CallToolResult SetColorToken(
        [Description("Document ID (GUID) or file path within working directory")] string idOrPath,
        [Description("Token name")] string name,
        [Description("Color value (e.g., #ff0000)")] string color,
        [Description("Expected revision for optimistic concurrency (optional)")] long? expectedRevn = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxNameLength ||
            color.Length > 1024 || expectedRevn is < 0)
        {
            return ToolResult.Fail(EngineErrors.InvalidProperty, "Token input is outside the allowed range");
        }

        var (handle, error) = OpenConfined(idOrPath, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (handle is null)
        {
            return DocumentNotFound(idOrPath);
        }

        try
        {
            _engine.SetColorToken(handle.Document, name, color);
            Save(handle, expectedRevn, cancellationToken);

            return ToolResult.Success(new
            {
                name,
                color,
                revn = handle.Document.Revn
            });
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return ToolResult.FromException(ex, "TOKEN_SET_FAILED");
        }
    }

    [McpServerTool(Title = "Remove Color Token")]
    [Description("Remove a color token from a document")]
    public CallToolResult RemoveColorToken(
        [Description("Document ID (GUID) or file path within working directory")] string idOrPath,
        [Description("Token name")] string name,
        [Description("Expected revision for optimistic concurrency (optional)")] long? expectedRevn = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxNameLength || expectedRevn is < 0)
        {
            return ToolResult.Fail(EngineErrors.InvalidProperty, "Token input is outside the allowed range");
        }

        var (handle, error) = OpenConfined(idOrPath, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (handle is null)
        {
            return DocumentNotFound(idOrPath);
        }

        try
        {
            var removed = _engine.RemoveColorToken(handle.Document, name);
            if (!removed)
                return ToolResult.Fail("TOKEN_NOT_FOUND", $"Token not found: {name}");

            Save(handle, expectedRevn, cancellationToken);

            return ToolResult.Success(new
            {
                name,
                revn = handle.Document.Revn
            });
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return ToolResult.FromException(ex, "TOKEN_REMOVE_FAILED");
        }
    }

    [McpServerTool(Title = "List Color Tokens")]
    [Description("List all color tokens in a document")]
    public CallToolResult ListColorTokens(
        [Description("Document ID (GUID) or file path within working directory")] string idOrPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (handle, error) = OpenConfined(idOrPath, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (handle is null)
        {
            return DocumentNotFound(idOrPath);
        }

        var colors = _engine.GetColorTokens(handle.Document);

        return ToolResult.Success(new
        {
            count = colors.Count,
            tokens = colors.Select(kv => new { name = kv.Key, color = kv.Value }).ToArray()
        });
    }

    [McpServerTool(Title = "Apply Palette")]
    [Description("Apply a bundled color palette to a document")]
    public CallToolResult ApplyPalette(
        [Description("Document ID (GUID) or file path within working directory")] string idOrPath,
        [Description("Palette name (tailwind, material, colorbrewer-qualitative)")] string palette,
        [Description("Expected revision for optimistic concurrency (optional)")] long? expectedRevn = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(palette) || palette.Length > MaxNameLength || expectedRevn is < 0)
        {
            return ToolResult.Fail(EngineErrors.InvalidProperty, "Palette input is outside the allowed range");
        }

        var (handle, error) = OpenConfined(idOrPath, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (handle is null)
        {
            return DocumentNotFound(idOrPath);
        }

        try
        {
            var result = _engine.ApplyPalette(handle.Document, palette);
            if (!result.Success)
                return ToolResult.Fail(result.ErrorCode!, result.ErrorMessage!);

            Save(handle, expectedRevn, cancellationToken);

            return ToolResult.Success(new
            {
                palette = result.PaletteName,
                colorCount = result.ColorCount,
                revn = handle.Document.Revn
            });
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return ToolResult.FromException(ex, "PALETTE_APPLY_FAILED");
        }
    }

    [McpServerTool(Title = "List Palettes")]
    [Description("List available bundled color palettes")]
    public CallToolResult ListPalettes(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var palettes = _engine.ListPalettes();

        return ToolResult.Success(new
        {
            count = palettes.Count,
            palettes = palettes.Select(p => new
            {
                name = p.Name,
                description = p.Description,
                license = p.License,
                colorCount = p.ColorCount
            }).ToArray()
        });
    }

    [McpServerTool(Title = "Import DTCG Tokens")]
    [Description("Import tokens from a DTCG format JSON file")]
    public async Task<CallToolResult> ImportDtcgTokens(
        [Description("Document ID (GUID) or file path within working directory")] string idOrPath,
        [Description("Path to DTCG tokens JSON file")] string tokensPath,
        [Description("Expected revision for optimistic concurrency (optional)")] long? expectedRevn = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (expectedRevn is < 0)
        {
            return ToolResult.Fail(EngineErrors.InvalidProperty, "Expected revision cannot be negative");
        }

        var (handle, error) = OpenConfined(idOrPath, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (handle is null)
        {
            return DocumentNotFound(idOrPath);
        }

        var tokensResult = _pathConfinement.Confine(tokensPath);
        if (!tokensResult.Success)
            return ToolResult.Fail(EngineErrors.PathEscape, tokensResult.Error!);

        if (!File.Exists(tokensResult.Path!))
            return ToolResult.Fail("FILE_NOT_FOUND", $"Tokens file not found: {tokensPath}");

        if (new FileInfo(tokensResult.Path!).Length > MaxInputFileBytes)
            return ToolResult.Fail("INPUT_TOO_LARGE", $"Tokens input exceeds the {MaxInputFileBytes} byte limit");

        try
        {
            var json = await File.ReadAllTextAsync(tokensResult.Path!, cancellationToken);
            var result = _engine.ImportDtcgTokens(handle.Document, json, cancellationToken);
            if (!result.Success)
                return ToolResult.Fail(result.ErrorCode!, result.ErrorMessage!);

            Save(handle, expectedRevn, cancellationToken);

            return ToolResult.Success(new
            {
                colorCount = result.ColorCount,
                typographyCount = result.TypographyCount,
                revn = handle.Document.Revn
            });
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return ToolResult.FromException(ex, "TOKEN_IMPORT_FAILED");
        }
    }

    [McpServerTool(Title = "Export DTCG Tokens")]
    [Description("Export tokens from a document to DTCG format JSON")]
    public async Task<CallToolResult> ExportDtcgTokens(
        [Description("Document ID (GUID) or file path within working directory")] string idOrPath,
        [Description("Output file path (default: derived from input)")] string? output = null,
        [Description("Resolve token references to actual values")] bool resolveRefs = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (handle, error) = OpenConfined(idOrPath, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (handle is null)
        {
            return DocumentNotFound(idOrPath);
        }

        var result = _engine.ExportDtcgTokens(handle.Document, resolveRefs);
        if (!result.Success || result.Content is null)
            return ToolResult.Fail(result.ErrorCode ?? "EXPORT_FAILED", result.ErrorMessage ?? "Export failed");

        if (Encoding.UTF8.GetByteCount(result.Content) > MaxOutputBytes)
            return ToolResult.Fail("OUTPUT_TOO_LARGE", "Token output exceeds the allowed limit");

        string outputPath;
        if (output is not null)
        {
            outputPath = output;
        }
        else if (handle.SourcePath is not null)
        {
            outputPath = DeriveTokensOutputPath(handle.SourcePath);
        }
        else
        {
            // Document opened by GUID without source path - derive from document name
            outputPath = $"{handle.Document.Name}.tokens.json";
        }
        var outputResult = _pathConfinement.Confine(outputPath);
        if (!outputResult.Success)
            return ToolResult.Fail(EngineErrors.PathEscape, outputResult.Error!);

        var outputDirectory = Path.GetDirectoryName(outputResult.Path!);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }
        var recheckedOutput = _pathConfinement.Confine(outputResult.Path!);
        if (!recheckedOutput.Success)
            return ToolResult.Fail(EngineErrors.PathEscape, recheckedOutput.Error!);

        await WriteAllBytesAtomicallyAsync(recheckedOutput.Path!, Encoding.UTF8.GetBytes(result.Content), cancellationToken);

        return ToolResult.Success(new
        {
            path = _pathConfinement.RelativePath(recheckedOutput.Path!),
            resolveRefs
        });
    }

    private static string DeriveTokensOutputPath(string inputPath)
    {
        var dir = Path.GetDirectoryName(inputPath) ?? ".";
        var name = Path.GetFileNameWithoutExtension(inputPath);
        if (name.EndsWith(".agd", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        return Path.Combine(dir, $"{name}.tokens.json");
    }

    // ---- Layout: bounds and alignment helpers ----

    [McpServerTool(Title = "Get Bounds")]
    [Description("Report local (untransformed) and world (after every transform, clipped by clipping frames) bounds for one node, or for every node when nodeId is omitted. Text bounds come from the shaping engine; measured=false marks estimated text.")]
    public CallToolResult GetBounds(
        [Description("Document ID (GUID) or file path within working directory")] string idOrPath,
        [Description("Node ID (GUID); omit to report every node")] string? nodeId = null,
        [Description("Comma-separated directories within the working directory whose .ttf/.otf files are searched before the bundled Inter family and system fonts when measuring text")] string? fontDirs = null,
        [Description("Allow host-installed fonts for families that the font directories and bundled Inter do not provide (default: true)")] bool systemFonts = true,
        [Description("Treat an unresolved font family as a hard failure instead of substituting bundled Inter")] bool strictFonts = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (fontDirectories, fontError) = ConfineFontDirectories(fontDirs);
        if (fontError is not null)
        {
            return fontError;
        }

        var (handle, error) = OpenConfined(idOrPath, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (handle is null)
        {
            return DocumentNotFound(idOrPath);
        }

        Guid? nodeGuid = null;
        if (nodeId is not null)
        {
            if (!Guid.TryParse(nodeId, out var parsed))
            {
                return ToolResult.Fail(EngineErrors.NodeNotFound, $"Node not found: {nodeId}");
            }

            nodeGuid = parsed;
        }

        try
        {
            var bounds = _engine.GetBounds(handle.Document, nodeGuid,
                BuildFontPolicy(fontDirectories, systemFonts, strictFonts), cancellationToken);
            return ToolResult.Success(new
            {
                documentId = handle.Document.Id,
                documentName = handle.Document.Name,
                nodeCount = bounds.Count,
                nodes = bounds.Select(LayoutPayload.Node).ToArray()
            });
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return ToolResult.FromException(ex, "BOUNDS_FAILED");
        }
    }

    [McpServerTool(Title = "Align Nodes", ReadOnly = false)]
    [Description("Align nodes by their world bounds. Changes only transform.x/transform.y. Nodes must share a parent unless relativeTo is canvas.")]
    public CallToolResult AlignNodes(
        [Description("Document ID (GUID) or file path within working directory")] string idOrPath,
        [Description("JSON array of node ID strings (GUIDs), at most 1000")] string[] nodeIds,
        [Description("Align mode: left, centerX, right, top, centerY, bottom")] string mode,
        [Description("Reference: parent, selection, canvas (default: selection; parent for a single node)")] string? relativeTo = null,
        [Description("Expected revision for optimistic concurrency (optional)")] long? expectedRevn = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (expectedRevn is < 0)
        {
            return ToolResult.Fail(EngineErrors.InvalidProperty, "Expected revision cannot be negative");
        }
        if (TooLong(mode, MaxNameLength) || TooLong(relativeTo, MaxNameLength))
        {
            return ToolResult.Fail("INPUT_TOO_LARGE", "One or more text inputs exceed the allowed limit");
        }
        if (!TryParseNodeIds(nodeIds, out var ids, out var idsError))
        {
            return ToolResult.Fail(EngineErrors.InvalidArguments, idsError!);
        }
        if (!LayoutOptions.TryParseAlignMode(mode, out var alignMode))
        {
            return ToolResult.Fail(EngineErrors.InvalidArguments,
                $"Unknown align mode: '{mode}'. Supported: {string.Join(", ", LayoutOptions.AlignModes)}");
        }

        AlignReference? reference = null;
        if (relativeTo is not null)
        {
            if (!LayoutOptions.TryParseAlignReference(relativeTo, out var parsedReference))
            {
                return ToolResult.Fail(EngineErrors.InvalidArguments,
                    $"Unknown align reference: '{relativeTo}'. Supported: {string.Join(", ", LayoutOptions.AlignReferences)}");
            }

            reference = parsedReference;
        }

        var (handle, error) = OpenConfined(idOrPath, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (handle is null)
        {
            return DocumentNotFound(idOrPath);
        }

        try
        {
            var result = _engine.AlignNodes(handle.Document, new AlignRequest
            {
                NodeIds = ids,
                Mode = alignMode,
                RelativeTo = reference
            }, cancellationToken);

            Save(handle, expectedRevn, cancellationToken);

            return ToolResult.Success(new
            {
                mode = LayoutOptions.Name(result.Mode),
                relativeTo = LayoutOptions.Name(result.RelativeTo),
                reference = LayoutPayload.Box(result.Reference),
                nodes = result.Nodes.Select(LayoutPayload.Placement).ToArray(),
                documentRevn = handle.Document.Revn
            });
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return ToolResult.FromException(ex, "ALIGN_FAILED");
        }
    }

    [McpServerTool(Title = "Distribute Nodes", ReadOnly = false)]
    [Description("Space nodes evenly along one axis by gap or by centers. Needs three or more nodes, or two with an explicit gap. Changes only transform.x/transform.y.")]
    public CallToolResult DistributeNodes(
        [Description("Document ID (GUID) or file path within working directory")] string idOrPath,
        [Description("JSON array of node ID strings (GUIDs), at most 1000")] string[] nodeIds,
        [Description("Axis: horizontal, vertical")] string axis,
        [Description("Spacing mode: gap (default), centers")] string mode = "gap",
        [Description("Explicit spacing; when omitted the first and last nodes stay fixed and spacing is derived")] double? gap = null,
        [Description("Expected revision for optimistic concurrency (optional)")] long? expectedRevn = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (expectedRevn is < 0)
        {
            return ToolResult.Fail(EngineErrors.InvalidProperty, "Expected revision cannot be negative");
        }
        if (gap is { } gapValue && !double.IsFinite(gapValue))
        {
            return ToolResult.Fail(EngineErrors.InvalidProperty, "Gap must be finite");
        }
        if (TooLong(axis, MaxNameLength) || TooLong(mode, MaxNameLength))
        {
            return ToolResult.Fail("INPUT_TOO_LARGE", "One or more text inputs exceed the allowed limit");
        }
        if (!TryParseNodeIds(nodeIds, out var ids, out var idsError))
        {
            return ToolResult.Fail(EngineErrors.InvalidArguments, idsError!);
        }
        if (!LayoutOptions.TryParseDistributeAxis(axis, out var distributeAxis))
        {
            return ToolResult.Fail(EngineErrors.InvalidArguments,
                $"Unknown axis: '{axis}'. Supported: {string.Join(", ", LayoutOptions.DistributeAxes)}");
        }
        if (!LayoutOptions.TryParseDistributeMode(mode, out var distributeMode))
        {
            return ToolResult.Fail(EngineErrors.InvalidArguments,
                $"Unknown distribute mode: '{mode}'. Supported: {string.Join(", ", LayoutOptions.DistributeModes)}");
        }

        var (handle, error) = OpenConfined(idOrPath, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (handle is null)
        {
            return DocumentNotFound(idOrPath);
        }

        try
        {
            var result = _engine.DistributeNodes(handle.Document, new DistributeRequest
            {
                NodeIds = ids,
                Axis = distributeAxis,
                Mode = distributeMode,
                Gap = gap
            }, cancellationToken);

            Save(handle, expectedRevn, cancellationToken);

            return ToolResult.Success(new
            {
                axis = LayoutOptions.Name(result.Axis),
                mode = LayoutOptions.Name(result.Mode),
                gap = result.Gap,
                nodes = result.Nodes.Select(LayoutPayload.Placement).ToArray(),
                documentRevn = handle.Document.Revn
            });
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return ToolResult.FromException(ex, "DISTRIBUTE_FAILED");
        }
    }

    [McpServerTool(Title = "Fit Frame", ReadOnly = false)]
    [Description("Resize a frame to enclose its children plus padding, shifting the children so their visual position is unchanged.")]
    public CallToolResult FitFrame(
        [Description("Document ID (GUID) or file path within working directory")] string idOrPath,
        [Description("Frame node ID (GUID)")] string frameId,
        [Description("Padding around the children (default: 0)")] double padding = 0,
        [Description("Expected revision for optimistic concurrency (optional)")] long? expectedRevn = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (expectedRevn is < 0)
        {
            return ToolResult.Fail(EngineErrors.InvalidProperty, "Expected revision cannot be negative");
        }
        if (!double.IsFinite(padding) || padding < 0)
        {
            return ToolResult.Fail(EngineErrors.InvalidProperty, "Padding must be finite and non-negative");
        }

        var (handle, error) = OpenConfined(idOrPath, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (handle is null)
        {
            return DocumentNotFound(idOrPath);
        }

        if (!Guid.TryParse(frameId, out var frameGuid))
        {
            return ToolResult.Fail(EngineErrors.NodeNotFound, $"Node not found: {frameId}");
        }

        try
        {
            var result = _engine.FitFrame(handle.Document, new FitRequest
            {
                FrameId = frameGuid,
                Padding = padding
            }, cancellationToken);

            Save(handle, expectedRevn, cancellationToken);

            return ToolResult.Success(new
            {
                frameId = result.FrameId,
                frameName = result.FrameName,
                x = result.Frame.X,
                y = result.Frame.Y,
                width = result.Frame.Width,
                height = result.Frame.Height,
                previous = LayoutPayload.Box(result.Previous),
                children = result.Children.Select(LayoutPayload.Placement).ToArray(),
                documentRevn = handle.Document.Revn
            });
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return ToolResult.FromException(ex, "FIT_FAILED");
        }
    }

    // ---- Helper methods ----

    private static bool TryParseNodeIds(string[]? nodeIds, out IReadOnlyList<Guid> ids, out string? error)
    {
        ids = [];
        if (nodeIds is null || nodeIds.Length > NodeIdList.MaxCount ||
            nodeIds.Any(id => id is null || id.Length > 64))
        {
            error = "Node IDs must be a bounded JSON array of GUID strings";
            return false;
        }
        return NodeIdList.TryParse(JsonSerializer.Serialize(nodeIds), out ids, out error);
    }

    private void SaveCreatedDocument(
        Document document,
        string path,
        bool force,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var rechecked = _pathConfinement.Confine(path);
        if (!rechecked.Success)
        {
            throw new DocumentEngineException(EngineErrors.PathEscape, rechecked.Error!);
        }

        if (force)
        {
            _engine.SaveDocumentToFile(document, rechecked.Path!, expectedRevn: null, cancellationToken);
        }
        else
        {
            _engine.CreateDocumentFile(document, rechecked.Path!, cancellationToken);
        }
    }

    private static async Task WriteAllBytesAtomicallyAsync(
        string outputPath,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(outputPath)
            ?? throw new IOException("Output path has no parent directory");
        var temporaryPath = Path.Combine(directory, $".agd-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.Asynchronous))
            {
                await stream.WriteAsync(data, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, outputPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private (string? source, CallToolResult? error) ConfineImageSource(string? source) =>
        TryConfineImageSource(source, out var normalized, out var error)
            ? (normalized, null)
            : (null, ToolResult.Fail(EngineErrors.PathEscape, error!));

    private bool TryConfineImageSource(string? source, out string? normalized, out string? error)
    {
        error = null;
        normalized = source;
        if (string.IsNullOrWhiteSpace(source))
        {
            return true;
        }

        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is not "file")
        {
            return true;
        }

        var path = uri?.LocalPath ?? source;
        var confined = _pathConfinement.Confine(path);
        if (!confined.Success)
        {
            normalized = null;
            error = confined.Error;
            return false;
        }

        normalized = _pathConfinement.RelativePath(confined.Path!);
        return true;
    }

    private void Save(
        DocumentHandle handle,
        long? expectedRevn,
        CancellationToken cancellationToken)
    {
        EnsureConfinedSource(handle);
        _engine.Save(handle, expectedRevn, cancellationToken);
    }

    private IEnumerable<string> EnumerateDocumentFiles(CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(_pathConfinement.WorkingDirectory);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var confinedDirectory = _pathConfinement.Confine(pending.Pop());
            if (!confinedDirectory.Success)
            {
                continue;
            }
            var directory = confinedDirectory.Path!;
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var confined = _pathConfinement.Confine(entry);
                if (!confined.Success)
                {
                    continue;
                }

                var attributes = File.GetAttributes(confined.Path!);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    continue;
                }
                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    pending.Push(confined.Path!);
                }
                else if (confined.Path!.EndsWith(".agd.json", StringComparison.OrdinalIgnoreCase))
                {
                    yield return confined.Path!;
                }
            }
        }
    }

    private static bool AllFinite(params double?[] values) =>
        values.All(value => !value.HasValue || double.IsFinite(value.Value));

    private static bool TooLong(string? value, int maximum = MaxTextLength) =>
        value?.Length > maximum;

    private static bool IsExpected(Exception exception) => ToolResult.IsExpectedException(exception);

    private static bool TryParseBooleanOp(string value, out BooleanOperation operation)
    {
        operation = value.ToLowerInvariant() switch
        {
            "union" => BooleanOperation.Union,
            "intersect" or "intersection" => BooleanOperation.Intersect,
            "difference" or "diff" => BooleanOperation.Difference,
            "xor" => BooleanOperation.Xor,
            _ => (BooleanOperation)(-1)
        };
        return (int)operation >= 0;
    }

    private static bool TryParseJoinType(string value, out OffsetJoinType joinType)
    {
        joinType = value.ToLowerInvariant() switch
        {
            "miter" => OffsetJoinType.Miter,
            "round" => OffsetJoinType.Round,
            "square" => OffsetJoinType.Square,
            _ => (OffsetJoinType)(-1)
        };
        return (int)joinType >= 0;
    }

    private static bool TryParseEndType(string value, out OffsetEndType endType)
    {
        endType = value.ToLowerInvariant() switch
        {
            "polygon" => OffsetEndType.Polygon,
            "joined" => OffsetEndType.Joined,
            "butt" => OffsetEndType.Butt,
            "square" => OffsetEndType.Square,
            "round" => OffsetEndType.Round,
            _ => (OffsetEndType)(-1)
        };
        return (int)endType >= 0;
    }
}
