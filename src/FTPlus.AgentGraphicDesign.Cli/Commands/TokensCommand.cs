using System.ComponentModel;
using FTPlus.AgentGraphicDesign.Cli.Output;
using FTPlus.AgentGraphicDesign.Cli.Infrastructure;
using FTPlus.AgentGraphicDesign.Core;
using Spectre.Console.Cli;

namespace FTPlus.AgentGraphicDesign.Cli.Commands;

public sealed class TokensSetColorCommand : Command<TokensSetColorCommand.Settings>
{
    private readonly IDocumentEngine _engine;

    public TokensSetColorCommand(IDocumentEngine engine)
    {
        _engine = engine;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<DOCUMENT>")]
        [Description("Path to .agd.json document")]
        public required string Document { get; init; }

        [CommandArgument(1, "<NAME>")]
        [Description("Token name")]
        public required string Name { get; init; }

        [CommandArgument(2, "<COLOR>")]
        [Description("Color value (e.g., #ff0000)")]
        public required string Color { get; init; }

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
        if (string.IsNullOrWhiteSpace(settings.Name) || settings.Name.Length > SurfaceIO.MaxNameLength ||
            settings.Color.Length > 1024 || settings.ExpectedRevn is < 0)
        {
            AgentOutput.WriteError(EngineErrors.InvalidProperty, "Token input is outside the allowed range", format);
            return ExitCodes.InvalidArguments;
        }

        var handle = SurfaceIO.OpenDocument(_engine, settings.Document, ct);
        if (handle is null)
        {
            AgentOutput.WriteError(EngineErrors.DocumentNotFound, $"Document not found: {settings.Document}", format);
            return ExitCodes.GeneralError;
        }

        _engine.SetColorToken(handle.Document, settings.Name, settings.Color);
        SurfaceIO.Save(_engine, handle, settings.ExpectedRevn, ct);

        AgentOutput.WriteSuccess($"Set color token '{settings.Name}' = {settings.Color}", format, new
        {
            name = settings.Name,
            color = settings.Color,
            revn = handle.Document.Revn
        });

        return ExitCodes.Success;
    }
}

public sealed class TokensRemoveColorCommand : Command<TokensRemoveColorCommand.Settings>
{
    private readonly IDocumentEngine _engine;

    public TokensRemoveColorCommand(IDocumentEngine engine)
    {
        _engine = engine;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<DOCUMENT>")]
        [Description("Path to .agd.json document")]
        public required string Document { get; init; }

        [CommandArgument(1, "<NAME>")]
        [Description("Token name")]
        public required string Name { get; init; }

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
        if (string.IsNullOrWhiteSpace(settings.Name) || settings.Name.Length > SurfaceIO.MaxNameLength ||
            settings.ExpectedRevn is < 0)
        {
            AgentOutput.WriteError(EngineErrors.InvalidProperty, "Token input is outside the allowed range", format);
            return ExitCodes.InvalidArguments;
        }

        var handle = SurfaceIO.OpenDocument(_engine, settings.Document, ct);
        if (handle is null)
        {
            AgentOutput.WriteError(EngineErrors.DocumentNotFound, $"Document not found: {settings.Document}", format);
            return ExitCodes.GeneralError;
        }

        var removed = _engine.RemoveColorToken(handle.Document, settings.Name);
        if (!removed)
        {
            AgentOutput.WriteError("TOKEN_NOT_FOUND", $"Token not found: {settings.Name}", format);
            return ExitCodes.GeneralError;
        }

        SurfaceIO.Save(_engine, handle, settings.ExpectedRevn, ct);

        AgentOutput.WriteSuccess($"Removed color token '{settings.Name}'", format, new
        {
            name = settings.Name,
            revn = handle.Document.Revn
        });

        return ExitCodes.Success;
    }
}

public sealed class TokensListColorsCommand : Command<TokensListColorsCommand.Settings>
{
    private readonly IDocumentEngine _engine;

    public TokensListColorsCommand(IDocumentEngine engine)
    {
        _engine = engine;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<DOCUMENT>")]
        [Description("Path to .agd.json document")]
        public required string Document { get; init; }

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

        var colors = _engine.GetColorTokens(handle.Document);

        AgentOutput.WriteSuccess($"{colors.Count} color token(s)", format, new
        {
            count = colors.Count,
            tokens = colors.Select(kv => new { name = kv.Key, color = kv.Value }).ToArray()
        });

        return ExitCodes.Success;
    }
}

public sealed class TokensApplyPaletteCommand : Command<TokensApplyPaletteCommand.Settings>
{
    private readonly IDocumentEngine _engine;

    public TokensApplyPaletteCommand(IDocumentEngine engine)
    {
        _engine = engine;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<DOCUMENT>")]
        [Description("Path to .agd.json document")]
        public required string Document { get; init; }

        [CommandArgument(1, "<PALETTE>")]
        [Description("Palette name (tailwind, material, colorbrewer-qualitative)")]
        public required string Palette { get; init; }

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
        if (settings.Palette.Length > SurfaceIO.MaxNameLength || settings.ExpectedRevn is < 0)
        {
            AgentOutput.WriteError(EngineErrors.InvalidProperty, "Palette input is outside the allowed range", format);
            return ExitCodes.InvalidArguments;
        }

        var handle = SurfaceIO.OpenDocument(_engine, settings.Document, ct);
        if (handle is null)
        {
            AgentOutput.WriteError(EngineErrors.DocumentNotFound, $"Document not found: {settings.Document}", format);
            return ExitCodes.GeneralError;
        }

        var result = _engine.ApplyPalette(handle.Document, settings.Palette);
        if (!result.Success)
        {
            AgentOutput.WriteError(result.ErrorCode!, result.ErrorMessage!, format);
            return ExitCodes.GeneralError;
        }

        SurfaceIO.Save(_engine, handle, settings.ExpectedRevn, ct);

        AgentOutput.WriteSuccess($"Applied palette '{result.PaletteName}' ({result.ColorCount} colors)", format, new
        {
            palette = result.PaletteName,
            colorCount = result.ColorCount,
            revn = handle.Document.Revn
        });

        return ExitCodes.Success;
    }
}

public sealed class TokensListPalettesCommand : Command<TokensListPalettesCommand.Settings>
{
    private readonly IDocumentEngine _engine;

    public TokensListPalettesCommand(IDocumentEngine engine)
    {
        _engine = engine;
    }

    public sealed class Settings : CommandSettings
    {
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
        var palettes = _engine.ListPalettes();

        AgentOutput.WriteSuccess($"{palettes.Count} palette(s) available", format, new
        {
            count = palettes.Count,
            palettes = palettes.Select(p => new
            {
                name = p.Name,
                description = p.Description,
                license = p.License,
                colorCount = p.ColorCount
            }).ToArray()
        });

        return ExitCodes.Success;
    }
}

public sealed class TokensImportCommand : AsyncCommand<TokensImportCommand.Settings>
{
    private readonly IDocumentEngine _engine;

    public TokensImportCommand(IDocumentEngine engine)
    {
        _engine = engine;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<DOCUMENT>")]
        [Description("Path to .agd.json document")]
        public required string Document { get; init; }

        [CommandArgument(1, "<TOKENS_FILE>")]
        [Description("Path to DTCG tokens JSON file")]
        public required string TokensFile { get; init; }

        [CommandOption("-f|--format <FORMAT>")]
        [Description("Output format: text, json (default: text)")]
        public string? Format { get; init; }

        [CommandOption("--expected-revn <REVN>")]
        [Description("Expected document revision (optimistic concurrency)")]
        public long? ExpectedRevn { get; init; }
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

        if (!File.Exists(settings.TokensFile))
        {
            AgentOutput.WriteError("FILE_NOT_FOUND", $"Tokens file not found: {settings.TokensFile}", format);
            return ExitCodes.GeneralError;
        }

        if (!SurfaceIO.TryCheckFileSize(settings.TokensFile, SurfaceIO.MaxInputFileBytes, out var sizeError))
        {
            AgentOutput.WriteError("INPUT_TOO_LARGE", sizeError!, format);
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

        var json = await File.ReadAllTextAsync(settings.TokensFile, ct);
        var result = _engine.ImportDtcgTokens(handle.Document, json, ct);
        if (!result.Success)
        {
            AgentOutput.WriteError(result.ErrorCode!, result.ErrorMessage!, format);
            return ExitCodes.GeneralError;
        }

        SurfaceIO.Save(_engine, handle, settings.ExpectedRevn, ct);

        AgentOutput.WriteSuccess($"Imported {result.ColorCount} color(s), {result.TypographyCount} typography token(s)", format, new
        {
            colorCount = result.ColorCount,
            typographyCount = result.TypographyCount,
            revn = handle.Document.Revn
        });

        return ExitCodes.Success;
    }
}

public sealed class TokensExportCommand : AsyncCommand<TokensExportCommand.Settings>
{
    private readonly IDocumentEngine _engine;

    public TokensExportCommand(IDocumentEngine engine)
    {
        _engine = engine;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<DOCUMENT>")]
        [Description("Path to .agd.json document")]
        public required string Document { get; init; }

        [CommandOption("-o|--output <PATH>")]
        [Description("Output .tokens.json file path (default: derived from input)")]
        public string? Output { get; init; }

        [CommandOption("--resolve-refs")]
        [Description("Resolve token references to actual values")]
        public bool ResolveRefs { get; init; }

        [CommandOption("-f|--format <FORMAT>")]
        [Description("Output format: text, json (default: text)")]
        public string? Format { get; init; }
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
        var handle = SurfaceIO.OpenDocument(_engine, settings.Document, ct);
        if (handle is null)
        {
            AgentOutput.WriteError(EngineErrors.DocumentNotFound, $"Document not found: {settings.Document}", format);
            return ExitCodes.GeneralError;
        }

        var result = _engine.ExportDtcgTokens(handle.Document, settings.ResolveRefs);
        if (!result.Success || result.Content is null)
        {
            AgentOutput.WriteError(result.ErrorCode ?? "EXPORT_FAILED", result.ErrorMessage ?? "Export failed", format);
            return ExitCodes.GeneralError;
        }

        var outputPath = settings.Output ?? DeriveOutputPath(settings.Document);
        if (result.Content.Length > SurfaceIO.MaxOutputBytes)
        {
            AgentOutput.WriteError("OUTPUT_TOO_LARGE", "Token output exceeds the allowed limit", format);
            return ExitCodes.GeneralError;
        }
        await File.WriteAllTextAsync(outputPath, result.Content, ct);

        AgentOutput.WriteSuccess($"Exported tokens to: {outputPath}", format, new
        {
            path = SurfaceIO.DisplayPath(outputPath),
            resolvedRefs = settings.ResolveRefs
        });

        return ExitCodes.Success;
    }

    private static string DeriveOutputPath(string inputPath)
    {
        var dir = Path.GetDirectoryName(inputPath) ?? ".";
        var name = Path.GetFileNameWithoutExtension(inputPath);
        if (name.EndsWith(".agd", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        return Path.Combine(dir, $"{name}.tokens.json");
    }
}
