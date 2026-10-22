using System.ComponentModel;
using FTPlus.AgentGraphicDesign.Cli.Output;
using FTPlus.AgentGraphicDesign.Cli.Infrastructure;
using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Serialization;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FTPlus.AgentGraphicDesign.Cli.Commands;

public sealed class ListCommand : Command<ListCommand.Settings>
{
    private readonly IDocumentEngine _engine;

    public ListCommand(IDocumentEngine engine)
    {
        _engine = engine;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[DOCUMENT]")]
        [Description("Document file path or ID. If omitted, lists documents in current directory.")]
        public string? Document { get; init; }

        [CommandOption("--tree")]
        [Description("Display as tree structure (only for node listing)")]
        public bool Tree { get; init; }

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

        if (string.IsNullOrEmpty(settings.Document))
        {
            return ListDocuments(format, ct);
        }

        return ListNodes(settings, format, ct);
    }

    private int ListDocuments(OutputFormat format, CancellationToken cancellationToken)
    {
        var currentDir = Directory.GetCurrentDirectory();
        var documents = new List<DocumentSummary>();

        foreach (var file in Directory.EnumerateFiles(currentDir, "*.agd.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!SurfaceIO.TryCheckFileSize(file, SurfaceIO.MaxInputFileBytes, out _))
                {
                    continue;
                }
                var result = _engine.TryLoadDocumentFromFile(file, cancellationToken);
                if (result.Success && result.Document is not null)
                {
                    var fileInfo = new FileInfo(file);
                    documents.Add(new DocumentSummary(
                        result.Document.Id,
                        result.Document.Name,
                        Path.GetFileName(file),
                        result.Document.Revn,
                        fileInfo.LastWriteTimeUtc));
                }
            }
            catch (IOException)
            {
                // Skip unreadable files
            }
        }

        if (format == OutputFormat.Json)
        {
            AgentOutput.WriteData(new
            {
                directory = currentDir,
                count = documents.Count,
                documents = documents.Select(d => new
                {
                    id = d.Id,
                    name = d.Name,
                    path = d.Path,
                    revn = d.Revn,
                    updatedAt = d.UpdatedAt
                })
            }, format);
        }
        else
        {
            if (documents.Count == 0)
            {
                AnsiConsole.MarkupLine("[grey]No .agd.json documents found in current directory[/]");
                return ExitCodes.Success;
            }

            var table = new Table();
            table.AddColumn("Name");
            table.AddColumn("File");
            table.AddColumn("ID");
            table.AddColumn("Rev");
            table.AddColumn("Updated");

            foreach (var doc in documents.OrderBy(d => d.Name))
            {
                table.AddRow(
                    Markup.Escape(doc.Name),
                    Markup.Escape(doc.Path),
                    Markup.Escape(doc.Id.ToString()[..8] + "..."),
                    doc.Revn.ToString(),
                    doc.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                );
            }

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"[dim]{documents.Count} document(s) found[/]");
        }

        return ExitCodes.Success;
    }

    private int ListNodes(Settings settings, OutputFormat format, CancellationToken cancellationToken)
    {
        var handle = SurfaceIO.OpenDocument(_engine, settings.Document!, cancellationToken);
        if (handle is null)
        {
            AgentOutput.WriteError(EngineErrors.DocumentNotFound, $"Document not found: {settings.Document}", format);
            return ExitCodes.GeneralError;
        }

        var nodes = _engine.ListNodes(handle.Document);

        if (format == OutputFormat.Json)
        {
            var result = new
            {
                documentId = handle.Document.Id,
                documentName = handle.Document.Name,
                nodeCount = nodes.Count,
                nodes = nodes.Select(n => new
                {
                    id = n.Id,
                    type = n.Type,
                    name = n.Name,
                    path = n.Path,
                    order = n.Order,
                    depth = n.Depth,
                    visible = n.Visible
                })
            };
            AgentOutput.WriteData(result, format);
        }
        else if (settings.Tree)
        {
            WriteTree(handle.Document, nodes);
        }
        else
        {
            WriteTable(nodes);
        }

        return ExitCodes.Success;
    }

    private static void WriteTree(Document document, IReadOnlyList<NodeInfo> nodes)
    {
        AnsiConsole.MarkupLine($"[bold]{Markup.Escape(document.Name)}[/] ({document.Id})");

        foreach (var node in nodes)
        {
            var indent = new string(' ', node.Depth * 2);
            var name = string.IsNullOrEmpty(node.Name) ? "[grey](unnamed)[/]" : Markup.Escape(node.Name);
            var visibility = node.Visible ? "" : " [grey](hidden)[/]";
            AnsiConsole.MarkupLine($"{indent}[cyan]{node.Type}[/] {name} [dim]{node.Id}[/]{visibility}");
        }
    }

    private static void WriteTable(IReadOnlyList<NodeInfo> nodes)
    {
        var table = new Table();
        table.AddColumn("ID");
        table.AddColumn("Type");
        table.AddColumn("Name");
        table.AddColumn("Path");
        table.AddColumn("Visible");

        foreach (var node in nodes)
        {
            table.AddRow(
                Markup.Escape(node.Id.ToString()),
                Markup.Escape(node.Type),
                Markup.Escape(node.Name ?? ""),
                Markup.Escape(node.Path),
                node.Visible ? "yes" : "no"
            );
        }

        AnsiConsole.Write(table);
    }

    private sealed record DocumentSummary(Guid Id, string Name, string Path, long Revn, DateTimeOffset UpdatedAt);
}
