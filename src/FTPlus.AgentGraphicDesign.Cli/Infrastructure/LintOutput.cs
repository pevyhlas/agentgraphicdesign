using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Lint;
using FTPlus.AgentGraphicDesign.Core.Validation;

namespace FTPlus.AgentGraphicDesign.Cli.Infrastructure;

/// <summary>JSON shapes for lint results, mirrored by the MCP <c>lint_document</c> tool.</summary>
internal static class LintOutput
{
    public static object Diagnostic(LintDiagnostic diagnostic) => new
    {
        code = diagnostic.Code,
        severity = LintSeverities.Name(diagnostic.Severity),
        message = diagnostic.Message,
        nodeId = diagnostic.NodeId,
        details = diagnostic.Details
    };

    public static object Result(DocumentHandle handle, string locator, LintResult result) => new
    {
        documentId = handle.Document.Id,
        documentName = handle.Document.Name,
        locator,
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
