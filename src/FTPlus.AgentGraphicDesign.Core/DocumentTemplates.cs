using System.Diagnostics.CodeAnalysis;

namespace FTPlus.AgentGraphicDesign.Core;

/// <summary>
/// A named canvas preset for document creation. Templates only seed the canvas
/// (size and background); the created document is otherwise identical to a plain
/// <see cref="IDocumentEngine.CreateDocument(string, double, double, string?, CreateDocumentOptions?)"/> call.
/// </summary>
public sealed record DocumentTemplate(
    string Id,
    double Width,
    double Height,
    string? Background,
    string Description);

/// <summary>
/// The shared template catalog behind <c>agd create --template</c> and the MCP
/// <c>create_document</c> <c>template</c> argument. Both surfaces resolve ids here so
/// the catalog has one source of truth.
/// </summary>
public static class DocumentTemplates
{
    public const string WebIcon32 = "web-icon-32";
    public const string WebIcon24 = "web-icon-24";
    public const string Social1200x630 = "social-1200x630";

    /// <summary>All templates in presentation order.</summary>
    public static IReadOnlyList<DocumentTemplate> All { get; } =
    [
        new(WebIcon32, 32, 32, null, "32x32 web icon with a transparent canvas (validate with --profile web-icon)"),
        new(WebIcon24, 24, 24, null, "24x24 web icon with a transparent canvas (validate with --profile web-icon)"),
        new(Social1200x630, 1200, 630, "#ffffff", "1200x630 social/link-preview card with a white background"),
    ];

    /// <summary>Comma-separated ids for help text and error messages.</summary>
    public static string IdList { get; } = string.Join(", ", All.Select(template => template.Id));

    /// <summary>Looks up a template by exact id (ordinal, surrounding whitespace ignored).</summary>
    public static bool TryGet(string? id, [NotNullWhen(true)] out DocumentTemplate? template)
    {
        var trimmed = id?.Trim();
        if (!string.IsNullOrEmpty(trimmed))
        {
            foreach (var candidate in All)
            {
                if (string.Equals(candidate.Id, trimmed, StringComparison.Ordinal))
                {
                    template = candidate;
                    return true;
                }
            }
        }

        template = null;
        return false;
    }

    /// <summary>
    /// Resolves a template id or throws <see cref="DocumentEngineException"/> with
    /// <see cref="EngineErrors.InvalidArguments"/> and the list of valid ids.
    /// </summary>
    public static DocumentTemplate Get(string id)
    {
        if (TryGet(id, out var template))
        {
            return template;
        }

        throw new DocumentEngineException(EngineErrors.InvalidArguments,
            $"Unknown template '{id}'. Valid templates: {IdList}");
    }

    /// <summary>
    /// Combines an optional template with optional explicit canvas values. Explicit
    /// values win over the template; the engine defaults (1920x1080, no background)
    /// apply when neither is present.
    /// </summary>
    public static CanvasSpec ResolveCanvas(DocumentTemplate? template, double? width, double? height, string? background) =>
        new(
            width ?? template?.Width ?? DefaultWidth,
            height ?? template?.Height ?? DefaultHeight,
            background ?? template?.Background);

    public const double DefaultWidth = 1920;
    public const double DefaultHeight = 1080;
}

/// <summary>Resolved canvas inputs for document creation.</summary>
public readonly record struct CanvasSpec(double Width, double Height, string? Background);
