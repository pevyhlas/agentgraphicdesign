using System.ComponentModel;
using FTPlus.AgentGraphicDesign.Cli.Infrastructure;
using FTPlus.AgentGraphicDesign.Cli.Output;
using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Layout;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FTPlus.AgentGraphicDesign.Cli.Commands;

public sealed class BoundsCommand : Command<BoundsCommand.Settings>
{
    private readonly IDocumentEngine _engine;

    public BoundsCommand(IDocumentEngine engine)
    {
        _engine = engine;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<DOCUMENT>")]
        [Description("Document file path or ID")]
        public required string Document { get; init; }

        [CommandOption("-n|--node <ID>")]
        [Description("Node ID (GUID); omit to report every node")]
        public string? Node { get; init; }

        [CommandOption("--font-dir <DIR>")]
        [Description("Directory of .ttf/.otf files searched before the bundled Inter family and system fonts when measuring text (repeatable)")]
        public string[]? FontDirs { get; init; }

        [CommandOption("--no-system-fonts")]
        [Description("Never use host-installed fonts when measuring; unresolved families substitute bundled Inter")]
        public bool NoSystemFonts { get; init; }

        [CommandOption("--strict-fonts")]
        [Description("Treat an unresolved font family as a hard failure instead of substituting bundled Inter")]
        public bool StrictFonts { get; init; }

        [CommandOption("-f|--format <FORMAT>")]
        [Description("Output format: text, json (default: text)")]
        public string? Format { get; init; }
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

        var handle = SurfaceIO.OpenDocument(_engine, settings.Document, ct);
        if (handle is null)
        {
            AgentOutput.WriteError(EngineErrors.DocumentNotFound, $"Document not found: {settings.Document}", format);
            return ExitCodes.GeneralError;
        }

        Guid? nodeId = null;
        if (settings.Node is not null)
        {
            if (!Guid.TryParse(settings.Node, out var parsed))
            {
                AgentOutput.WriteError(EngineErrors.NodeNotFound, $"Invalid node ID: {settings.Node}", format);
                return ExitCodes.GeneralError;
            }

            nodeId = parsed;
        }

        var fontResult = FontPolicyOptions.Resolve(settings.FontDirs, settings.NoSystemFonts, settings.StrictFonts);
        if (!fontResult.Success)
        {
            AgentOutput.WriteError(fontResult.ErrorCode!, fontResult.ErrorMessage!, format);
            return ExitCodes.InvalidArguments;
        }

        try
        {
            var bounds = _engine.GetBounds(handle.Document, nodeId, fontResult.Policy, ct);

            if (format == OutputFormat.Json)
            {
                AgentOutput.WriteData(new
                {
                    documentId = handle.Document.Id,
                    documentName = handle.Document.Name,
                    nodeCount = bounds.Count,
                    nodes = bounds.Select(LayoutOutput.Node)
                }, format);
            }
            else
            {
                WriteTable(bounds);
            }

            return ExitCodes.Success;
        }
        catch (DocumentEngineException ex)
        {
            AgentOutput.WriteError(ex.Code, ex.Message, format);
            return ExitCodes.GeneralError;
        }
    }

    private static void WriteTable(IReadOnlyList<NodeBounds> bounds)
    {
        var table = new Table();
        table.AddColumn("Type");
        table.AddColumn("Name");
        table.AddColumn("ID");
        table.AddColumn("Local (x,y wxh)");
        table.AddColumn("World (x,y wxh)");
        table.AddColumn("Notes");

        foreach (var node in bounds)
        {
            var notes = new List<string>();
            if (node.Measured == false)
                notes.Add("estimated");
            if (node.Clipped)
                notes.Add("clipped");
            if (!node.Visible)
                notes.Add("hidden");
            notes.AddRange(node.Diagnostics);

            table.AddRow(
                Markup.Escape(node.Type),
                Markup.Escape(node.Name ?? ""),
                Markup.Escape(node.Id.ToString()),
                Markup.Escape(LayoutOutput.Format(node.Local)),
                Markup.Escape(LayoutOutput.Format(node.World)),
                Markup.Escape(string.Join("; ", notes)));
        }

        AnsiConsole.Write(table);
    }
}
