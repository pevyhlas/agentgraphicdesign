using System.ComponentModel;
using FTPlus.AgentGraphicDesign.Cli.Output;
using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Artifacts;
using Spectre.Console.Cli;

namespace FTPlus.AgentGraphicDesign.Cli.Commands;

public sealed class AssetsPreflightCommand : AsyncCommand<AssetsPreflightCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<DESCRIPTOR>")]
        [Description("Artifact descriptor in a dedicated bundle directory; payload paths resolve from that directory")]
        public required string Descriptor { get; init; }

        [CommandOption("-f|--format <FORMAT>")]
        [Description("Output format: text, json (default: text)")]
        public string? Format { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken ct)
    {
        var parsed = OutputFormatExtensions.Parse(settings.Format);
        if (!parsed.Success)
        {
            AgentOutput.WriteError("INVALID_FORMAT", "Supported output formats are text and json.", OutputFormat.Json);
            return ExitCodes.InvalidArguments;
        }

        try
        {
            var result = await ArtifactPreflight.CheckAsync(settings.Descriptor, ct);
            if (parsed.Format == OutputFormat.Json)
            {
                AgentOutput.WriteJson(OutputEnvelope.Success(result));
            }
            else
            {
                Console.WriteLine("Artifact preflight passed: encoded integrity and PNG structure only.");
                Console.WriteLine($"Asset: {result.AssetId}; profile: {result.Profile}.");
                Console.WriteLine($"Canvas: {result.Width} x {result.Height}; files: {result.FileCount}; frames: {result.FrameCount}; encoded bytes: {result.TotalBytes}.");
                Console.WriteLine($"Metadata digest: {result.MetadataDigest}");
                Console.WriteLine($"PNG policy: {result.PngPolicy}");
                Console.WriteLine("Pixels not decoded; artifacts not admitted; provenance not verified.");
            }
            return ExitCodes.Success;
        }
        catch (DocumentEngineException ex)
        {
            AgentOutput.WriteError(ex.Code, ex.Message, parsed.Format);
            return ex.Code is EngineErrors.InvalidArguments or EngineErrors.UnsupportedVersion
                ? ExitCodes.InvalidArguments : ExitCodes.GeneralError;
        }
        catch (OperationCanceledException)
        {
            AgentOutput.WriteError("CANCELLED", "Operation cancelled", parsed.Format);
            return 130;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AgentOutput.WriteError("ARTIFACT_INPUT_UNAVAILABLE", "Artifact input is unavailable.", parsed.Format);
            return ExitCodes.GeneralError;
        }
    }
}
