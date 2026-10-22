using System.ComponentModel;
using FTPlus.AgentGraphicDesign.Cli.Output;
using FTPlus.AgentGraphicDesign.Cli.Infrastructure;
using FTPlus.AgentGraphicDesign.Core;
using Spectre.Console.Cli;

namespace FTPlus.AgentGraphicDesign.Cli.Commands;

public sealed class MoveCommand : Command<MoveCommand.Settings>
{
    private readonly IDocumentEngine _engine;

    public MoveCommand(IDocumentEngine engine)
    {
        _engine = engine;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<DOCUMENT>")]
        [Description("Document file path or ID")]
        public required string Document { get; init; }

        [CommandArgument(1, "<NODE_ID>")]
        [Description("Node ID (GUID) to move")]
        public required string NodeId { get; init; }

        [CommandArgument(2, "<NEW_PARENT>")]
        [Description("New parent node ID (GUID), or 'root' for root level")]
        public required string NewParent { get; init; }

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

        var handle = SurfaceIO.OpenDocument(_engine, settings.Document, ct);
        if (handle is null)
        {
            AgentOutput.WriteError(EngineErrors.DocumentNotFound, $"Document not found: {settings.Document}", format);
            return ExitCodes.GeneralError;
        }

        if (!Guid.TryParse(settings.NodeId, out var nodeId))
        {
            AgentOutput.WriteError(EngineErrors.NodeNotFound, $"Invalid node ID: {settings.NodeId}", format);
            return ExitCodes.GeneralError;
        }

        Guid? parentId = null;
        if (!settings.NewParent.Equals("root", StringComparison.OrdinalIgnoreCase))
        {
            if (!Guid.TryParse(settings.NewParent, out var parsed))
            {
                AgentOutput.WriteError(EngineErrors.ParentNotFound, $"Invalid parent ID: {settings.NewParent}", format);
                return ExitCodes.GeneralError;
            }

            parentId = parsed;
        }

        try
        {
            var node = _engine.MoveNode(handle.Document, nodeId, parentId);
            SurfaceIO.Save(_engine, handle, settings.ExpectedRevn, ct);

            var result = new
            {
                nodeId = node.Id,
                nodeName = node.Name,
                newOrder = node.Order,
                documentRevn = handle.Document.Revn
            };

            AgentOutput.WriteSuccess($"Moved node: {node.Id}", format, result);
            return ExitCodes.Success;
        }
        catch (DocumentEngineException ex)
        {
            AgentOutput.WriteError(ex.Code, ex.Message, format);
            return ExitCodes.GeneralError;
        }
    }
}
