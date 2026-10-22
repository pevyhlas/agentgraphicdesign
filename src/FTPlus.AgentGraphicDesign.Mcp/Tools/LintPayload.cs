using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Lint;
using FTPlus.AgentGraphicDesign.Core.Validation;

namespace FTPlus.AgentGraphicDesign.Mcp.Tools;

/// <summary>Result shapes for the lint tool, mirroring the CLI JSON output.</summary>
internal static class LintPayload
{
    public static object Diagnostic(LintDiagnostic diagnostic) => new
    {
        code = diagnostic.Code,
        severity = LintSeverities.Name(diagnostic.Severity),
        message = diagnostic.Message,
        nodeId = diagnostic.NodeId,
        details = diagnostic.Details
    };

    public static object Result(DocumentHandle handle, LintResult result) => new
    {
        documentId = handle.Document.Id,
        documentName = handle.Document.Name,
        profile = result.Profile == ValidationProfile.WebIcon ? ValidationProfiles.WebIcon : "default",
        targetSize = result.TargetSize,
        strictGrid = result.StrictGrid,
        gridChecked = result.GridChecked,
        warningCount = result.WarningCount,
        infoCount = result.InfoCount,
        truncated = result.Truncated,
        diagnostics = result.Diagnostics.Select(Diagnostic)
    };
}
