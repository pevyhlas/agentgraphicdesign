using System.ComponentModel;
using System.Globalization;
using FTPlus.AgentGraphicDesign.Cli.Infrastructure;
using FTPlus.AgentGraphicDesign.Cli.Output;
using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Lint;
using FTPlus.AgentGraphicDesign.Core.Validation;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FTPlus.AgentGraphicDesign.Cli.Commands;

/// <summary>
/// Advisory design lints (LINT001-LINT007). Unlike <c>validate</c>, lint never blocks
/// export: the command exits 0 unless <c>--fail-on</c> names a severity that was reported.
/// </summary>
public sealed class LintCommand : Command<LintCommand.Settings>
{
    private readonly IDocumentEngine _engine;

    public LintCommand(IDocumentEngine engine)
    {
        _engine = engine;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<DOCUMENT>")]
        [Description("Document file path or ID")]
        public required string Document { get; init; }

        [CommandOption("--profile <PROFILE>")]
        [Description("Lint profile: web-icon (forces the grid rule and enables symbol readiness)")]
        public string? Profile { get; init; }

        [CommandOption("--target-size <PIXELS>")]
        [Description("Pixel size the document will be shown at along its larger edge; scales the hairline rule (default: native size)")]
        public double? TargetSize { get; init; }

        [CommandOption("--strict-grid")]
        [Description("Off-grid rule requires whole pixels (warning) instead of half pixels (info)")]
        public bool StrictGrid { get; init; }

        [CommandOption("--fail-on <SEVERITY>")]
        [Description("Exit non-zero when a finding of this severity or above exists: warning, info")]
        public string? FailOn { get; init; }

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

        if (!ValidationProfiles.TryParse(settings.Profile, out var profile))
        {
            AgentOutput.WriteError(EngineErrors.InvalidArguments,
                $"Unknown lint profile. Supported: {ValidationProfiles.WebIcon}", format);
            return ExitCodes.InvalidArguments;
        }

        LintSeverity? failOn = null;
        if (settings.FailOn is not null)
        {
            if (!LintSeverities.TryParse(settings.FailOn, out var severity))
            {
                AgentOutput.WriteError(EngineErrors.InvalidArguments,
                    $"Unknown --fail-on severity. Supported: {LintSeverities.Warning}, {LintSeverities.Info}", format);
                return ExitCodes.InvalidArguments;
            }

            failOn = severity;
        }

        if (settings.TargetSize is { } targetSize && (!double.IsFinite(targetSize) || targetSize <= 0))
        {
            AgentOutput.WriteError(EngineErrors.InvalidArguments, "--target-size must be a positive number of pixels", format);
            return ExitCodes.InvalidArguments;
        }

        var fontResult = FontPolicyOptions.Resolve(settings.FontDirs, settings.NoSystemFonts, settings.StrictFonts);
        if (!fontResult.Success)
        {
            AgentOutput.WriteError(fontResult.ErrorCode!, fontResult.ErrorMessage!, format);
            return ExitCodes.InvalidArguments;
        }

        try
        {
            var handle = SurfaceIO.OpenDocument(_engine, settings.Document, ct);
            if (handle is null)
            {
                AgentOutput.WriteError(EngineErrors.DocumentNotFound, $"Document not found: {settings.Document}", format);
                return ExitCodes.GeneralError;
            }

            var options = new LintOptions
            {
                Profile = profile,
                TargetSize = settings.TargetSize,
                StrictGrid = settings.StrictGrid
            };
            var result = _engine.Lint(handle.Document, options, fontResult.Policy, ct);
            var locator = handle.SourcePath is { } source ? SurfaceIO.DisplayPath(source) : settings.Document;

            if (format == OutputFormat.Json)
            {
                AgentOutput.WriteData(LintOutput.Result(handle, locator, result), format);
            }
            else
            {
                WriteText(result);
            }

            var failed = failOn switch
            {
                LintSeverity.Warning => result.WarningCount > 0,
                LintSeverity.Info => result.Diagnostics.Count > 0,
                _ => false
            };
            return failed ? ExitCodes.LintFailed : ExitCodes.Success;
        }
        catch (DocumentEngineException ex)
        {
            AgentOutput.WriteError(ex.Code, ex.Message, format);
            return ex.Code is EngineErrors.InvalidArguments or EngineErrors.InvalidProperty
                ? ExitCodes.InvalidArguments
                : ExitCodes.GeneralError;
        }
    }

    private static void WriteText(LintResult result)
    {
        if (result.Diagnostics.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]✓[/] No lint findings");
            return;
        }

        var table = new Table();
        table.AddColumn("Severity");
        table.AddColumn("Code");
        table.AddColumn("Node");
        table.AddColumn("Message");

        foreach (var diagnostic in result.Diagnostics)
        {
            var severity = diagnostic.Severity == LintSeverity.Warning
                ? "[yellow]warning[/]"
                : "[blue]info[/]";
            table.AddRow(
                severity,
                Markup.Escape(diagnostic.Code),
                Markup.Escape(diagnostic.NodeId?.ToString() ?? "-"),
                Markup.Escape(diagnostic.Message));
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine(
            $"{result.WarningCount} warning(s), {result.InfoCount} info(s)" +
            $" (target size {result.TargetSize.ToString("0.##", CultureInfo.InvariantCulture)} px" +
            $"{(result.GridChecked ? ", grid checked" : "")})");
        if (result.Truncated)
        {
            AnsiConsole.MarkupLine("[yellow]![/] Findings were truncated");
        }
    }
}
