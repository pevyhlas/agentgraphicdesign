using System.ComponentModel;
using FTPlus.AgentGraphicDesign.Cli.Output;
using FTPlus.AgentGraphicDesign.Core.Pixels;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FTPlus.AgentGraphicDesign.Cli.Commands;

public sealed class PixelImportCommand(NativePixelService service) : PixelCommandBase<PixelImportCommand.Settings>(service)
{
    public sealed class Settings : PixelDirectorySettings
    {
        [CommandOption("--descriptor <FILE>")]
        [Description("Original provided-artifact v1 descriptor for one PNG still")]
        public required string Descriptor { get; init; }
        [CommandOption("--name <NAME>")]
        [Description("Name for the new native asset")]
        public required string Name { get; init; }
        [CommandOption("--layer-name <NAME>")]
        [Description("Name for the imported independent layer")]
        public required string LayerName { get; init; }
    }

    protected override async Task<int> RunAsync(Settings settings, OutputFormat format, CancellationToken ct)
    {
        var result = await Service.ImportAsync(new(
            NativePixelWire.NormalizePath(settings.Directory, Environment.CurrentDirectory),
            NativePixelWire.NormalizePath(settings.Descriptor, Environment.CurrentDirectory), settings.Name, settings.LayerName), ct);
        if (format == OutputFormat.Json) AgentOutput.WriteData(result, format);
        else
        {
            AnsiConsole.MarkupLine("[green]✓[/] Imported and preserved a native still asset (managed local; provenance unverified)");
            WriteToken(result.State.Snapshot);
            AnsiConsole.MarkupLine($"  layer: {result.InitialLayerId}  record: {result.Import.RecordPath}");
        }
        return ExitCodes.Success;
    }
}

public sealed class PixelImportLayerCommand(NativePixelService service) : PixelCommandBase<PixelImportLayerCommand.Settings>(service)
{
    public sealed class Settings : PixelDirectorySettings
    {
        [CommandOption("--request <FILE>")]
        [Description("Original JSON {descriptorPath,expectedSnapshot,layerName}; paths resolve from invocation directory")]
        public required string Request { get; init; }
    }

    protected override async Task<int> RunAsync(Settings settings, OutputFormat format, CancellationToken ct)
    {
        var result = await Service.ImportLayerFileAsync(settings.Directory, settings.Request, Environment.CurrentDirectory, ct);
        if (format == OutputFormat.Json) AgentOutput.WriteData(result, format);
        else
        {
            AnsiConsole.MarkupLine("[green]✓[/] Imported and preserved an independent layer (managed local; provenance unverified)");
            WriteToken(result.State.Snapshot);
            AnsiConsole.MarkupLine($"  layer: {result.LayerId}  record: {result.Import.RecordPath}");
        }
        return ExitCodes.Success;
    }
}
