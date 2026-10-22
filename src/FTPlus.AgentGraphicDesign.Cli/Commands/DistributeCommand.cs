using System.ComponentModel;
using FTPlus.AgentGraphicDesign.Cli.Infrastructure;
using FTPlus.AgentGraphicDesign.Cli.Output;
using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Layout;
using Spectre.Console.Cli;

namespace FTPlus.AgentGraphicDesign.Cli.Commands;

public sealed class DistributeCommand : Command<DistributeCommand.Settings>
{
    private readonly IDocumentEngine _engine;

    public DistributeCommand(IDocumentEngine engine)
    {
        _engine = engine;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<DOCUMENT>")]
        [Description("Document file path or ID")]
        public required string Document { get; init; }

        [CommandOption("--nodes <IDS>")]
        [Description("Node IDs (GUIDs), comma-separated or as a JSON array string; three or more, or two with --gap (required)")]
        public string? Nodes { get; init; }

        [CommandOption("--axis <AXIS>")]
        [Description("Axis: horizontal, vertical (required)")]
        public string? Axis { get; init; }

        [CommandOption("--mode <MODE>")]
        [Description("Spacing mode: gap (default), centers")]
        public string? Mode { get; init; }

        [CommandOption("--gap <N>")]
        [Description("Explicit spacing; when omitted the first and last nodes stay fixed")]
        public double? Gap { get; init; }

        [CommandOption("-f|--format <FORMAT>")]
        [Description("Output format: text, json (default: text)")]
        public string? Format { get; init; }

        [CommandOption("--expected-revn <REVN>")]
        [Description("Expected document revision (optimistic concurrency)")]
        public long? ExpectedRevn { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken ct)
    {
        var parseResult = OutputFormatExtensions.Parse(settings.Format);
        if (!parseResult.Success)
        {
            AgentOutput.WriteError("INVALID_FORMAT", parseResult.Error!, OutputFormat.Json);
            return ExitCodes.InvalidArguments;
        }

        var format = parseResult.Format;

        if (settings.ExpectedRevn is < 0)
        {
            AgentOutput.WriteError(EngineErrors.InvalidProperty, "Expected revision cannot be negative", format);
            return ExitCodes.InvalidArguments;
        }

        if (settings.Gap is { } gap && !SurfaceIO.IsFinite(gap))
        {
            AgentOutput.WriteError(EngineErrors.InvalidProperty, "Gap must be finite", format);
            return ExitCodes.InvalidArguments;
        }

        if (string.IsNullOrWhiteSpace(settings.Nodes))
        {
            AgentOutput.WriteError(EngineErrors.InvalidArguments, "--nodes is required", format);
            return ExitCodes.InvalidArguments;
        }

        if (string.IsNullOrWhiteSpace(settings.Axis))
        {
            AgentOutput.WriteError(EngineErrors.InvalidArguments,
                $"--axis is required. Supported: {string.Join(", ", LayoutOptions.DistributeAxes)}", format);
            return ExitCodes.InvalidArguments;
        }

        if (!NodeIdList.TryParse(settings.Nodes, out var nodeIds, out var nodesError))
        {
            AgentOutput.WriteError(EngineErrors.InvalidArguments, nodesError!, format);
            return ExitCodes.InvalidArguments;
        }

        if (!LayoutOptions.TryParseDistributeAxis(settings.Axis, out var axis))
        {
            AgentOutput.WriteError(EngineErrors.InvalidArguments,
                $"Unknown axis: '{settings.Axis}'. Supported: {string.Join(", ", LayoutOptions.DistributeAxes)}", format);
            return ExitCodes.InvalidArguments;
        }

        var mode = DistributeMode.Gap;
        if (settings.Mode is not null && !LayoutOptions.TryParseDistributeMode(settings.Mode, out mode))
        {
            AgentOutput.WriteError(EngineErrors.InvalidArguments,
                $"Unknown distribute mode: '{settings.Mode}'. Supported: {string.Join(", ", LayoutOptions.DistributeModes)}", format);
            return ExitCodes.InvalidArguments;
        }

        var handle = SurfaceIO.OpenDocument(_engine, settings.Document, ct);
        if (handle is null)
        {
            AgentOutput.WriteError(EngineErrors.DocumentNotFound, $"Document not found: {settings.Document}", format);
            return ExitCodes.GeneralError;
        }

        try
        {
            var result = _engine.DistributeNodes(handle.Document, new DistributeRequest
            {
                NodeIds = nodeIds,
                Axis = axis,
                Mode = mode,
                Gap = settings.Gap
            }, ct);

            SurfaceIO.Save(_engine, handle, settings.ExpectedRevn, ct);

            var axisName = LayoutOptions.Name(result.Axis);
            var modeName = LayoutOptions.Name(result.Mode);
            AgentOutput.WriteSuccess(
                $"Distributed {result.Nodes.Count} node(s) {axisName} by {modeName} (gap {result.Gap})",
                format,
                new
                {
                    axis = axisName,
                    mode = modeName,
                    gap = result.Gap,
                    nodes = result.Nodes.Select(LayoutOutput.Placement),
                    documentRevn = handle.Document.Revn
                });
            return ExitCodes.Success;
        }
        catch (DocumentEngineException ex)
        {
            AgentOutput.WriteError(ex.Code, ex.Message, format);
            return ExitCodes.GeneralError;
        }
    }
}
