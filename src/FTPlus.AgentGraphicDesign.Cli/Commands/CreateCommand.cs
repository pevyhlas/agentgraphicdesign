using System.ComponentModel;
using FTPlus.AgentGraphicDesign.Cli.Output;
using FTPlus.AgentGraphicDesign.Cli.Infrastructure;
using FTPlus.AgentGraphicDesign.Core;
using Spectre.Console.Cli;

namespace FTPlus.AgentGraphicDesign.Cli.Commands;

public sealed class CreateCommand : AsyncCommand<CreateCommand.Settings>
{
    private readonly IDocumentEngine _engine;

    public CreateCommand(IDocumentEngine engine)
    {
        _engine = engine;
    }

    /// <summary>
    /// Help text for <c>--template</c>. The ids are spelled out so <c>agd create --help</c>
    /// documents the catalog; <c>CreateCommandTests</c> asserts it matches
    /// <see cref="DocumentTemplates.All"/>.
    /// </summary>
    public const string TemplateDescription =
        "Canvas template: web-icon-32, web-icon-24, social-1200x630 (explicit --width/--height/--background override it)";

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<NAME>")]
        [Description("Name for the new document")]
        public required string Name { get; init; }

        [CommandOption("-o|--output <PATH>")]
        [Description("Output file path (default: ./<name>.agd.json)")]
        public string? Output { get; init; }

        [CommandOption("-w|--width <WIDTH>")]
        [Description("Canvas width in pixels (default: 1920, or the template width)")]
        public double? Width { get; init; }

        [CommandOption("-h|--height <HEIGHT>")]
        [Description("Canvas height in pixels (default: 1080, or the template height)")]
        public double? Height { get; init; }

        [CommandOption("--background <COLOR>")]
        [Description("Canvas background color (e.g., #ffffff); overrides the template background")]
        public string? Background { get; init; }

        [CommandOption("--template <ID>")]
        [Description(TemplateDescription)]
        public string? Template { get; init; }

        [CommandOption("-f|--format <FORMAT>")]
        [Description("Output format: text, json (default: text)")]
        public string? Format { get; init; }

        [CommandOption("--seed <SEED>")]
        [Description("Seed for deterministic ID generation; enables reproducible output")]
        public int? Seed { get; init; }

        [CommandOption("--force")]
        [Description("Overwrite the output file if it already exists")]
        public bool Force { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken ct)
    {
        var parseResult = OutputFormatExtensions.Parse(settings.Format);
        if (!parseResult.Success)
        {
            AgentOutput.WriteError("INVALID_FORMAT", parseResult.Error!, OutputFormat.Json);
            return ExitCodes.InvalidArguments;
        }

        var format = parseResult.Format;

        if (string.IsNullOrWhiteSpace(settings.Name) || settings.Name.Length > SurfaceIO.MaxNameLength)
        {
            AgentOutput.WriteError("INVALID_NAME", $"Name must contain 1-{SurfaceIO.MaxNameLength} characters", format);
            return ExitCodes.InvalidArguments;
        }

        if (settings.Background?.Length > 1024)
        {
            AgentOutput.WriteError("INPUT_TOO_LARGE", "Background value exceeds the allowed limit", format);
            return ExitCodes.InvalidArguments;
        }

        DocumentTemplate? template = null;
        if (settings.Template is not null && !DocumentTemplates.TryGet(settings.Template, out template))
        {
            AgentOutput.WriteError(EngineErrors.InvalidArguments,
                $"Unknown template '{settings.Template}'. Valid templates: {DocumentTemplates.IdList}", format);
            return ExitCodes.InvalidArguments;
        }

        var canvas = DocumentTemplates.ResolveCanvas(template, settings.Width, settings.Height, settings.Background);
        if (!SurfaceIO.IsFinite(canvas.Width) || !SurfaceIO.IsFinite(canvas.Height) ||
            canvas.Width <= 0 || canvas.Height <= 0 || canvas.Width > 100_000 || canvas.Height > 100_000)
        {
            AgentOutput.WriteError(EngineErrors.InvalidDimensions,
                "Canvas dimensions must be finite and between 0 and 100000", format);
            return ExitCodes.InvalidArguments;
        }

        try
        {
            var options = settings.Seed.HasValue
                ? new CreateDocumentOptions { Seed = settings.Seed.Value }
                : null;
            var document = _engine.CreateDocument(settings.Name, canvas.Width, canvas.Height, canvas.Background, options);

            var outputPath = settings.Output ?? $"{SanitizeFileName(settings.Name)}.agd.json";
            await SurfaceIO.CreateDocumentFileAsync(_engine, document, outputPath, settings.Force, ct);

            var result = new
            {
                documentId = document.Id,
                name = document.Name,
                locator = SurfaceIO.DisplayPath(outputPath),
                template = template?.Id,
                canvas = new { width = document.Canvas.Width, height = document.Canvas.Height, background = document.Canvas.Background }
            };

            AgentOutput.WriteSuccess($"Created document: {outputPath}", format, result);
            return ExitCodes.Success;
        }
        catch (DocumentEngineException ex)
        {
            AgentOutput.WriteError(ex.Code, ex.Message, format);
            return ExitCodes.GeneralError;
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }
}
