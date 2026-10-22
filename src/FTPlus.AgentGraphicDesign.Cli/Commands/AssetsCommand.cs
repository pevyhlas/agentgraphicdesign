using System.ComponentModel;
using System.Text.Json;
using FTPlus.AgentGraphicDesign.Cli.Output;
using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Assets;
using Spectre.Console.Cli;

namespace FTPlus.AgentGraphicDesign.Cli.Commands;

public abstract class AssetsCommand(AssetPipeline pipeline) : AsyncCommand<AssetsCommand.Settings>
{
    protected abstract bool Check { get; }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<MANIFEST>")]
        [Description("Asset manifest path; all manifest paths resolve from its directory")]
        public required string Manifest { get; init; }

        [CommandOption("-f|--format <FORMAT>")]
        [Description("Output format: text, json (default: text)")]
        public string? Format { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken ct)
    {
        var parsed = OutputFormatExtensions.Parse(settings.Format);
        if (!parsed.Success)
        {
            AgentOutput.WriteError("INVALID_FORMAT", parsed.Error!, OutputFormat.Json);
            return ExitCodes.InvalidArguments;
        }

        try
        {
            var result = await pipeline.RunAsync(settings.Manifest, Check, ct);
            if (parsed.Format == OutputFormat.Json)
            {
                AgentOutput.WriteJson(new OutputEnvelope
                {
                    Ok = result.Success,
                    Data = result,
                    Error = result.Success ? null : new OutputError
                    {
                        Code = "ASSET_DRIFT",
                        Message = "Generated assets differ from the manifest; inspect file statuses"
                    }
                });
            }
            else
            {
                foreach (var file in result.Files)
                    Console.WriteLine($"{file.Status}: {JsonSerializer.Serialize(file.Path)}");
                foreach (var diagnostic in result.Diagnostics)
                    AgentOutput.WriteDiagnostic(diagnostic.Code,
                        $"{diagnostic.Source} -> {diagnostic.Output}: {diagnostic.Message}", parsed.Format);
                if (!result.Success)
                    AgentOutput.WriteError("ASSET_DRIFT", "Generated assets differ from the manifest; unexpected files are never deleted automatically", parsed.Format);
            }

            return result.Success ? ExitCodes.Success : ExitCodes.AssetDrift;
        }
        catch (DocumentEngineException ex)
        {
            AgentOutput.WriteError(ex.Code, ex.Message, parsed.Format);
            return ex.Code is EngineErrors.InvalidArguments or EngineErrors.UnsupportedVersion
                ? ExitCodes.InvalidArguments : ExitCodes.GeneralError;
        }
    }
}

public sealed class AssetsBuildCommand(AssetPipeline pipeline) : AssetsCommand(pipeline)
{
    protected override bool Check => false;
}

public sealed class AssetsCheckCommand(AssetPipeline pipeline) : AssetsCommand(pipeline)
{
    protected override bool Check => true;
}
