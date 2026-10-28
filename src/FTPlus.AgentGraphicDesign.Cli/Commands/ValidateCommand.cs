using System.ComponentModel;
using FTPlus.AgentGraphicDesign.Cli.Output;
using FTPlus.AgentGraphicDesign.Cli.Infrastructure;
using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Serialization;
using FTPlus.AgentGraphicDesign.Core.Validation;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FTPlus.AgentGraphicDesign.Cli.Commands;

public sealed class ValidateCommand : Command<ValidateCommand.Settings>
{
    private readonly IDocumentEngine _engine;

    public ValidateCommand([FromKeyedServices(Program.FileEngineServiceKey)] IDocumentEngine engine)
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

        [CommandOption("--strict")]
        [Description("Treat warnings as errors")]
        public bool Strict { get; init; }

        [CommandOption("--profile <PROFILE>")]
        [Description("Additional validation profile: web-icon")]
        public string? Profile { get; init; }
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

        if (!ValidationProfiles.TryParse(settings.Profile, out var profile))
        {
            AgentOutput.WriteError(EngineErrors.InvalidArguments,
                $"Unknown validation profile. Supported: {ValidationProfiles.WebIcon}", format);
            return ExitCodes.InvalidArguments;
        }

        if (!SurfaceIO.TryResolvePath(settings.File, out var inputPath) || !File.Exists(inputPath))
        {
            AgentOutput.WriteError("FILE_NOT_FOUND", $"File not found: {settings.File}", format);
            return ExitCodes.GeneralError;
        }
        if (!SurfaceIO.TryCheckFileSize(inputPath, SurfaceIO.MaxInputFileBytes, out var sizeError))
        {
            AgentOutput.WriteError("INPUT_TOO_LARGE", sizeError!, format);
            return ExitCodes.InvalidArguments;
        }

        var loadResult = _engine.TryLoadDocumentFromFile(inputPath, ct);

        if (!loadResult.Success || loadResult.Document is null)
        {
            var error = loadResult.Error;
            AgentOutput.WriteError("PARSE_ERROR", error?.Message ?? "Failed to parse document JSON", format, error?.Path);
            return ExitCodes.GeneralError;
        }

        var document = loadResult.Document;
        var result = _engine.Validate(document, new ValidationOptions { Profile = profile });
        var isValid = result.IsValid && (!settings.Strict || result.Warnings.Count == 0);

        if (format == OutputFormat.Json)
        {
            var data = new
            {
                valid = isValid,
                locator = SurfaceIO.DisplayPath(inputPath),
                errors = result.Errors.Select(e => new { e.Code, e.Message, e.Path }),
                warnings = result.Warnings.Select(w => new { w.Code, w.Message, w.Path })
            };
            AgentOutput.WriteData(data, format);
        }
        else
        {
            if (result.Errors.Count > 0)
            {
                AnsiConsole.MarkupLine("[red]Errors:[/]");
                foreach (var error in result.Errors)
                {
                    AnsiConsole.MarkupLine($"  [red]✗[/] [[[grey]{error.Code}[/]]] {Markup.Escape(error.Message)}");
                    if (error.Path is not null)
                        AnsiConsole.MarkupLine($"    at: {Markup.Escape(error.Path)}");
                }
            }

            if (result.Warnings.Count > 0)
            {
                AnsiConsole.MarkupLine("[yellow]Warnings:[/]");
                foreach (var warning in result.Warnings)
                {
                    AnsiConsole.MarkupLine($"  [yellow]![/] [[[grey]{warning.Code}[/]]] {Markup.Escape(warning.Message)}");
                    if (warning.Path is not null)
                        AnsiConsole.MarkupLine($"    at: {Markup.Escape(warning.Path)}");
                }
            }

            if (result.IsValid && result.Warnings.Count == 0)
            {
                AgentOutput.WriteSuccess("Document is valid", format);
            }
            else if (result.IsValid)
            {
                AnsiConsole.MarkupLine($"[green]✓[/] Valid with {result.Warnings.Count} warning(s)");
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]✗[/] Invalid: {result.Errors.Count} error(s), {result.Warnings.Count} warning(s)");
            }
        }

        if (!result.IsValid)
            return ExitCodes.ValidationFailed;

        if (settings.Strict && result.Warnings.Count > 0)
            return ExitCodes.ValidationWarnings;

        return ExitCodes.Success;
    }
}
