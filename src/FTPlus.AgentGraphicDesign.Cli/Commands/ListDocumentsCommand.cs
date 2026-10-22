using System.ComponentModel;
using FTPlus.AgentGraphicDesign.Cli.Output;
using FTPlus.AgentGraphicDesign.Cli.Infrastructure;
using FTPlus.AgentGraphicDesign.Core;
using Spectre.Console.Cli;

namespace FTPlus.AgentGraphicDesign.Cli.Commands;

public sealed class ListDocumentsCommand : Command<ListDocumentsCommand.Settings>
{
    private readonly IDocumentEngine _engine;

    public ListDocumentsCommand(IDocumentEngine engine)
    {
        _engine = engine;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandOption("-d|--directory <PATH>")]
        [Description("Directory to search (default: current directory)")]
        public string? Directory { get; init; }

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
        var directory = settings.Directory ?? System.IO.Directory.GetCurrentDirectory();

        if (!System.IO.Directory.Exists(directory))
        {
            AgentOutput.WriteError("DIRECTORY_NOT_FOUND", $"Directory not found: {directory}", format);
            return ExitCodes.GeneralError;
        }

        var documents = new List<object>();

        foreach (var file in System.IO.Directory.EnumerateFiles(directory, "*.agd.json"))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!SurfaceIO.TryCheckFileSize(file, SurfaceIO.MaxInputFileBytes, out _))
                {
                    continue;
                }
                var result = _engine.TryLoadDocumentFromFile(file, ct);
                if (result.Success && result.Document is not null)
                {
                    var fileInfo = new FileInfo(file);
                    documents.Add(new
                    {
                        id = result.Document.Id,
                        name = result.Document.Name,
                        path = Path.GetFileName(file),
                        revn = result.Document.Revn,
                        updatedAt = fileInfo.LastWriteTimeUtc
                    });
                }
            }
            catch (IOException)
            {
            }
        }

        AgentOutput.WriteSuccess($"{documents.Count} document(s) found", format, new
        {
            directory = Path.GetFullPath(directory),
            count = documents.Count,
            documents
        });

        return ExitCodes.Success;
    }
}
