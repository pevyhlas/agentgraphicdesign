using System.ComponentModel;
using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Pixels;
using FTPlus.AgentGraphicDesign.Core.Platform;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace FTPlus.AgentGraphicDesign.Mcp.Tools;

/// <summary>Small, I/O-free discovery over executable registrations and Core's platform registry.</summary>
[McpServerToolType]
public sealed class CapabilitiesTools(PathConfinement confinement)
{
    [McpServerTool(Title = "Get Capabilities", ReadOnly = true)]
    [Description("Discover this executable's version, working directory, platform qualification, formats, supported domains, limits and CLI-only batch operations before authoring")]
    public CallToolResult GetCapabilities() => ToolResult.Success(new
    {
        version = Program.GetVersion(),
        workingDirectory = confinement.WorkingDirectory,
        platform = PlatformProfile.Current.ToCapabilitiesJson(),
        tools = Program.GetToolNames().ToArray(),
        templates = DocumentTemplates.All.Select(template => template.Id).ToArray(),
        formats = new { vector = 1, nativePixels = 1, providedArtifact = 1 },
        domains = new
        {
            vector = true,
            layeredStillPixels = true,
            animation = false,
            tiles = false,
            providedArtifactAdmission = false,
            nativeImageImport = true,
            blenderExecution = false
        },
        cliOnly = new[] { "assets build", "assets check", "sprite" },
        nativeStillImport = new
        {
            profile = NativeImportRecord.LimitsProfile,
            decoderProfile = NativeImportRecord.DecoderProfile,
            interpretationProfile = NativeImportRecord.InterpretationProfile,
            execution = "managed-in-process",
            filesystemTrust = "trusted-quiescent-local",
            maxFiles = 1,
            maxMetadataBytes = 1_048_576,
            maxEncodedBytes = NativeStillInput.MaxEncodedBytes,
            maxDimension = 8192,
            maxPixels = 4_194_304,
            maxReservationBytes = NativeOperationScope.MaxReservationBytes,
            cooperativeDeadlineSeconds = 60,
            admitted = false,
            provenanceVerified = false
        },
        limits = new { patchOperations = 500, stdioMessageBytes = Core.Wire.AgdWireParser.MaxMessageBytes, nodePage = 1000, nodeScan = NodePageQuery.MaxScannedNodes, defaultPreviewLongestEdge = 512, explicitPreviewDimension = 2048 },
        resources = new[] { "agd://schema", "agd://format", "agd://agent-guide" }
    });
}
