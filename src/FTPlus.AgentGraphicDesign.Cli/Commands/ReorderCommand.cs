using System.ComponentModel;
using FTPlus.AgentGraphicDesign.Cli.Output;
using FTPlus.AgentGraphicDesign.Cli.Infrastructure;
using FTPlus.AgentGraphicDesign.Core;
using Spectre.Console.Cli;

namespace FTPlus.AgentGraphicDesign.Cli.Commands;

public sealed class ReorderCommand : Command<ReorderCommand.Settings>
{
    private readonly IDocumentEngine _engine;

    public ReorderCommand(IDocumentEngine engine)
    {
        _engine = engine;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<DOCUMENT>")]
        [Description("Document file path or ID")]
        public required string Document { get; init; }

        [CommandArgument(1, "<NODE_ID>")]
        [Description("Node ID (GUID) to reorder")]
        public required string NodeId { get; init; }

        [CommandOption("--before <ID>")]
        [Description("Place immediately before this sibling node ID")]
        public string? Before { get; init; }

        [CommandOption("--after <ID>")]
        [Description("Place immediately after this sibling node ID")]
        public string? After { get; init; }

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

        if (settings.Before is null && settings.After is null)
        {
            AgentOutput.WriteError(EngineErrors.InvalidProperty, "Specify --before, --after, or both", format);
            return ExitCodes.InvalidArguments;
        }

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

        Guid? beforeId = null;
        if (settings.Before is not null)
        {
            if (!Guid.TryParse(settings.Before, out var parsed))
            {
                AgentOutput.WriteError(EngineErrors.NodeNotFound, $"Invalid before node ID: {settings.Before}", format);
                return ExitCodes.GeneralError;
            }
            beforeId = parsed;
        }

        Guid? afterId = null;
        if (settings.After is not null)
        {
            if (!Guid.TryParse(settings.After, out var parsed))
            {
                AgentOutput.WriteError(EngineErrors.NodeNotFound, $"Invalid after node ID: {settings.After}", format);
                return ExitCodes.GeneralError;
            }
            afterId = parsed;
        }

        try
        {
            // Core accepts the lower-order bound first and upper-order bound second.
            // Conventional CLI --after is the lower bound; --before is the upper bound.
            var node = _engine.ReorderNode(handle.Document, nodeId, afterId, beforeId);
            SurfaceIO.Save(_engine, handle, settings.ExpectedRevn, ct);

            var result = new
            {
                nodeId = node.Id,
                nodeName = node.Name,
                newOrder = node.Order,
                documentRevn = handle.Document.Revn
            };

            AgentOutput.WriteSuccess($"Reordered node: {node.Id}", format, result);
            return ExitCodes.Success;
        }
        catch (DocumentEngineException ex)
        {
            AgentOutput.WriteError(ex.Code, ex.Message, format);
            return ExitCodes.GeneralError;
        }
    }
}
