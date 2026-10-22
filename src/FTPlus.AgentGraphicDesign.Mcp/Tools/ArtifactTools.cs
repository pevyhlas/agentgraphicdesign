using System.ComponentModel;
using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Artifacts;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace FTPlus.AgentGraphicDesign.Mcp.Tools;

[McpServerToolType]
public sealed class ArtifactTools(PathConfinement pathConfinement)
{
    [McpServerTool(Name = "verify_artifact", Title = "Verify Artifact", ReadOnly = true)]
    [Description("Read-only exact RGBA8 sample verification using managed-in-process decoding, not an OS sandbox. Artifacts are not admitted and provenance is not verified.")]
    public async Task<CallToolResult> VerifyArtifact(
        [Description("Descriptor path within the working directory, in a dedicated artifact bundle directory")] string descriptorPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var confined = pathConfinement.Confine(descriptorPath);
            if (!confined.Success)
                return confined.ErrorCode is "IO_ERROR" or "ACCESS_DENIED"
                    ? ToolResult.Fail("ARTIFACT_INPUT_UNAVAILABLE", "Artifact input is unavailable.")
                    : ToolResult.Fail(EngineErrors.PathEscape, confined.Error!);

            return ToolResult.Success(await ArtifactVerification.CheckAsync(confined.Path!, cancellationToken));
        }
        catch (Exception ex) when (ToolResult.IsExpectedException(ex))
        {
            return ex switch
            {
                DocumentEngineException or OperationCanceledException => ToolResult.FromException(ex, "ARTIFACT_VERIFY_FAILED"),
                IOException or UnauthorizedAccessException => ToolResult.Fail("ARTIFACT_INPUT_UNAVAILABLE", "Artifact input is unavailable."),
                _ => ToolResult.Fail("ARTIFACT_VERIFY_FAILED", "Artifact verification failed.")
            };
        }
    }

    [McpServerTool(Name = "preflight_artifact", Title = "Preflight Artifact", ReadOnly = true)]
    [Description("Read-only artifact encoded-integrity and PNG-structure preflight. Pixels are not decoded, artifacts are not admitted, and provenance is not verified.")]
    public async Task<CallToolResult> PreflightArtifact(
        [Description("Descriptor path within the working directory, in a dedicated artifact bundle directory")] string descriptorPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var confined = pathConfinement.Confine(descriptorPath);
            if (!confined.Success)
                return confined.ErrorCode is "IO_ERROR" or "ACCESS_DENIED"
                    ? ToolResult.Fail("ARTIFACT_INPUT_UNAVAILABLE", "Artifact input is unavailable.")
                    : ToolResult.Fail(EngineErrors.PathEscape, confined.Error!);

            return ToolResult.Success(await ArtifactPreflight.CheckAsync(confined.Path!, cancellationToken));
        }
        catch (Exception ex) when (ToolResult.IsExpectedException(ex))
        {
            return ex switch
            {
                DocumentEngineException or OperationCanceledException => ToolResult.FromException(ex, "ARTIFACT_PREFLIGHT_FAILED"),
                IOException or UnauthorizedAccessException => ToolResult.Fail("ARTIFACT_INPUT_UNAVAILABLE", "Artifact input is unavailable."),
                _ => ToolResult.Fail("ARTIFACT_PREFLIGHT_FAILED", "Artifact preflight failed.")
            };
        }
    }
}
