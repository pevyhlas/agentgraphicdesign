using System.ComponentModel;
using FTPlus.AgentGraphicDesign.Cli.Infrastructure;
using FTPlus.AgentGraphicDesign.Cli.Output;
using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Layout;
using Spectre.Console.Cli;

namespace FTPlus.AgentGraphicDesign.Cli.Commands;

public sealed class AlignCommand : Command<AlignCommand.Settings>
{
    private readonly IDocumentEngine _engine;

    public AlignCommand(IDocumentEngine engine)
    {
        _engine = engine;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<DOCUMENT>")]
        [Description("Document file path or ID")]
        public required string Document { get; init; }

        [CommandOption("--nodes <IDS>")]
        [Description("Node IDs (GUIDs), comma-separated or as a JSON array string (required)")]
        public string? Nodes { get; init; }

        [CommandOption("--mode <MODE>")]
        [Description("Align mode: left, centerX, right, top, centerY, bottom (required)")]
        public string? Mode { get; init; }

        [CommandOption("--relative-to <REFERENCE>")]
        [Description("Reference: parent, selection, canvas (default: selection; parent for a single node)")]
        public string? RelativeTo { get; init; }

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

        if (string.IsNullOrWhiteSpace(settings.Nodes))
        {
            AgentOutput.WriteError(EngineErrors.InvalidArguments, "--nodes is required", format);
            return ExitCodes.InvalidArguments;
        }

        if (string.IsNullOrWhiteSpace(settings.Mode))
        {
            AgentOutput.WriteError(EngineErrors.InvalidArguments,
                $"--mode is required. Supported: {string.Join(", ", LayoutOptions.AlignModes)}", format);
            return ExitCodes.InvalidArguments;
        }

        if (!NodeIdList.TryParse(settings.Nodes, out var nodeIds, out var nodesError))
        {
            AgentOutput.WriteError(EngineErrors.InvalidArguments, nodesError!, format);
            return ExitCodes.InvalidArguments;
        }

        if (!LayoutOptions.TryParseAlignMode(settings.Mode, out var mode))
        {
            AgentOutput.WriteError(EngineErrors.InvalidArguments,
                $"Unknown align mode: '{settings.Mode}'. Supported: {string.Join(", ", LayoutOptions.AlignModes)}", format);
            return ExitCodes.InvalidArguments;
        }

        AlignReference? relativeTo = null;
        if (settings.RelativeTo is not null)
        {
            if (!LayoutOptions.TryParseAlignReference(settings.RelativeTo, out var reference))
            {
                AgentOutput.WriteError(EngineErrors.InvalidArguments,
                    $"Unknown align reference: '{settings.RelativeTo}'. Supported: {string.Join(", ", LayoutOptions.AlignReferences)}", format);
                return ExitCodes.InvalidArguments;
            }

            relativeTo = reference;
        }

        var handle = SurfaceIO.OpenDocument(_engine, settings.Document, ct);
        if (handle is null)
        {
            AgentOutput.WriteError(EngineErrors.DocumentNotFound, $"Document not found: {settings.Document}", format);
            return ExitCodes.GeneralError;
        }

        try
        {
            var result = _engine.AlignNodes(handle.Document, new AlignRequest
            {
                NodeIds = nodeIds,
                Mode = mode,
                RelativeTo = relativeTo
            }, ct);

            SurfaceIO.Save(_engine, handle, settings.ExpectedRevn, ct);

            var modeName = LayoutOptions.Name(result.Mode);
            var referenceName = LayoutOptions.Name(result.RelativeTo);
            AgentOutput.WriteSuccess(
                $"Aligned {result.Nodes.Count} node(s) {modeName} relative to {referenceName}",
                format,
                new
                {
                    mode = modeName,
                    relativeTo = referenceName,
                    reference = LayoutOutput.Box(result.Reference),
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
