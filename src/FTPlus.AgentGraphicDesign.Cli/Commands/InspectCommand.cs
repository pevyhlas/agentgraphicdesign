using System.ComponentModel;
using FTPlus.AgentGraphicDesign.Cli.Output;
using FTPlus.AgentGraphicDesign.Cli.Infrastructure;
using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Serialization;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FTPlus.AgentGraphicDesign.Cli.Commands;

public sealed class InspectCommand : Command<InspectCommand.Settings>
{
    private readonly IDocumentEngine _engine;

    public InspectCommand(IDocumentEngine engine)
    {
        _engine = engine;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<FILE>")]
        [Description("Path to the .agd.json document file")]
        public required string File { get; init; }

        [CommandOption("-f|--format <FORMAT>")]
        [Description("Output format: text, json (default: text)")]
        public string? Format { get; init; }

        [CommandOption("--tree")]
        [Description("Show node tree structure")]
        public bool Tree { get; init; }
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

        if (!System.IO.File.Exists(settings.File))
        {
            AgentOutput.WriteError("FILE_NOT_FOUND", $"File not found: {settings.File}", format);
            return ExitCodes.GeneralError;
        }
        if (!SurfaceIO.TryCheckFileSize(settings.File, SurfaceIO.MaxInputFileBytes, out var sizeError))
        {
            AgentOutput.WriteError("INPUT_TOO_LARGE", sizeError!, format);
            return ExitCodes.InvalidArguments;
        }

        var loadResult = _engine.TryLoadDocumentFromFile(settings.File, ct);

        if (!loadResult.Success || loadResult.Document is null)
        {
            var error = loadResult.Error;
            AgentOutput.WriteError("PARSE_ERROR", error?.Message ?? "Failed to parse document", format, error?.Path);
            return ExitCodes.GeneralError;
        }

        var document = loadResult.Document;

        if (format == OutputFormat.Json)
        {
            AgentOutput.WriteData(BuildInspectResult(_engine, document), format);
        }
        else
        {
            WriteTextOutput(_engine, document, settings.Tree);
        }

        return ExitCodes.Success;
    }

    private static object BuildInspectResult(IDocumentEngine engine, Document document)
    {
        return new
        {
            id = document.Id,
            name = document.Name,
            version = document.Version,
            revn = document.Revn,
            canvas = new
            {
                width = document.Canvas.Width,
                height = document.Canvas.Height,
                background = document.Canvas.Background
            },
            nodeCount = engine.CountNodes(document),
            nodes = BuildNodeTree(document.Root)
        };
    }

    private static void WriteTextOutput(IDocumentEngine engine, Document document, bool showTree)
    {
        var table = new Table();
        table.AddColumn("Property");
        table.AddColumn("Value");
        table.Border(TableBorder.Rounded);

        table.AddRow("ID", document.Id.ToString());
        table.AddRow("Name", document.Name);
        table.AddRow("Version", document.Version.ToString());
        table.AddRow("Revision", document.Revn.ToString());
        table.AddRow("Canvas", $"{document.Canvas.Width}×{document.Canvas.Height}");
        if (document.Canvas.Background is not null)
            table.AddRow("Background", document.Canvas.Background);
        table.AddRow("Node Count", engine.CountNodes(document).ToString());

        AnsiConsole.Write(table);

        if (showTree)
        {
            AnsiConsole.WriteLine();
            var tree = new Tree($"[bold]{document.Name}[/]");
            BuildTreeNode(tree, document.Root);
            AnsiConsole.Write(tree);
        }
    }

    private static void BuildTreeNode(IHasTreeNodes parent, Node node)
    {
        var label = GetNodeLabel(node);
        var treeNode = parent.AddNode(label);

        if (node is IContainerNode container)
        {
            foreach (var child in container.Children.OrderBy(c => c.Order, StringComparer.Ordinal))
            {
                BuildTreeNode(treeNode, child);
            }
        }
    }

    private static string GetNodeLabel(Node node)
    {
        var type = DocumentEngine.GetNodeTypeName(node);
        var name = node.Name ?? node.Id.ToString()[..8];
        return $"[blue]{type}[/] {name}";
    }

    private static object BuildNodeTree(Node node)
    {
        var children = node is IContainerNode container
            ? container.Children.OrderBy(c => c.Order, StringComparer.Ordinal).Select(BuildNodeTree).ToArray()
            : null;

        return new
        {
            id = node.Id,
            type = DocumentEngine.GetNodeTypeName(node),
            name = node.Name,
            order = node.Order,
            visible = node.Visible,
            children
        };
    }
}
