using System.ComponentModel;
using FTPlus.AgentGraphicDesign.Cli.Output;
using FTPlus.AgentGraphicDesign.Cli.Infrastructure;
using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.PathOperations;
using Spectre.Console.Cli;

namespace FTPlus.AgentGraphicDesign.Cli.Commands;

/// <summary>
/// Path boolean operations and stroke offsetting via Clipper2.
/// </summary>
public sealed class PathBooleanCommand : Command<PathBooleanCommand.Settings>
{
    private readonly IDocumentEngine _engine;

    public PathBooleanCommand(IDocumentEngine engine)
    {
        _engine = engine;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<OPERATION>")]
        [Description("Boolean operation: union, intersect, difference, xor")]
        public required string Operation { get; init; }

        [CommandArgument(1, "<SUBJECT>")]
        [Description("Subject path (SVG path d attribute)")]
        public required string Subject { get; init; }

        [CommandArgument(2, "<CLIP>")]
        [Description("Clip path (SVG path d attribute)")]
        public required string Clip { get; init; }

        [CommandOption("--fill-rule <RULE>")]
        [Description("Fill rule for overlapping contours: nonzero, evenodd (default: nonzero)")]
        [DefaultValue("nonzero")]
        public string FillRule { get; init; } = "nonzero";

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

        if (settings.Subject.Length > SurfaceIO.MaxTextLength || settings.Clip.Length > SurfaceIO.MaxTextLength)
        {
            AgentOutput.WriteError("INPUT_TOO_LARGE", "Path input exceeds the allowed limit", format);
            return ExitCodes.InvalidArguments;
        }

        if (!TryParseOperation(settings.Operation, out var operation))
        {
            AgentOutput.WriteError("INVALID_OPERATION",
                $"Unknown operation: '{settings.Operation}'. Supported: union, intersect, difference, xor",
                format);
            return ExitCodes.InvalidArguments;
        }

        if (!PathFillRules.TryParse(settings.FillRule, out var fillRule))
        {
            AgentOutput.WriteError("INVALID_FILL_RULE",
                $"Unknown fill rule: '{settings.FillRule}'. Supported: nonzero, evenodd",
                format);
            return ExitCodes.InvalidArguments;
        }

        var result = _engine.PathBooleanOp(settings.Subject, settings.Clip, operation, fillRule, ct);

        if (!result.Success)
        {
            AgentOutput.WriteError("PATH_OP_FAILED", result.Error!, format);
            return ExitCodes.GeneralError;
        }

        var output = new
        {
            operation = settings.Operation.ToLowerInvariant(),
            fillRule = PathFillRules.ToName(fillRule),
            pathData = result.PathData
        };

        AgentOutput.WriteSuccess($"Path {settings.Operation} complete", format, output);
        return ExitCodes.Success;
    }

    private static bool TryParseOperation(string value, out BooleanOperation operation)
    {
        operation = value.ToLowerInvariant() switch
        {
            "union" => BooleanOperation.Union,
            "intersect" or "intersection" => BooleanOperation.Intersect,
            "difference" or "diff" => BooleanOperation.Difference,
            "xor" => BooleanOperation.Xor,
            _ => (BooleanOperation)(-1)
        };
        return (int)operation >= 0;
    }
}

/// <summary>
/// Stroke offset operation via Clipper2.
/// </summary>
public sealed class PathOffsetCommand : Command<PathOffsetCommand.Settings>
{
    private readonly IDocumentEngine _engine;

    public PathOffsetCommand(IDocumentEngine engine)
    {
        _engine = engine;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<PATH>")]
        [Description("Input path (SVG path d attribute)")]
        public required string Path { get; init; }

        [CommandArgument(1, "<OFFSET>")]
        [Description("Offset distance (positive = outward, negative = inward)")]
        public required double Offset { get; init; }

        [CommandOption("-j|--join <TYPE>")]
        [Description("Join type: miter, round, square (default: round)")]
        [DefaultValue("round")]
        public string Join { get; init; } = "round";

        [CommandOption("-e|--end <TYPE>")]
        [Description("End type: polygon, joined, butt, square, round (default: polygon)")]
        [DefaultValue("polygon")]
        public string End { get; init; } = "polygon";

        [CommandOption("-m|--miter-limit <LIMIT>")]
        [Description("Miter limit (default: 2.0)")]
        [DefaultValue(2.0)]
        public double MiterLimit { get; init; } = 2.0;

        [CommandOption("--fill-rule <RULE>")]
        [Description("Fill rule for overlapping contours: nonzero, evenodd (default: nonzero; evenodd resolves overlaps into holes before offsetting)")]
        [DefaultValue("nonzero")]
        public string FillRule { get; init; } = "nonzero";

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

        if (settings.Path.Length > SurfaceIO.MaxTextLength ||
            !SurfaceIO.IsFinite(settings.Offset) || !SurfaceIO.IsFinite(settings.MiterLimit) ||
            settings.MiterLimit is <= 0 or > 1000)
        {
            AgentOutput.WriteError(EngineErrors.InvalidProperty,
                "Path input or numeric offset parameters are outside the allowed range", format);
            return ExitCodes.InvalidArguments;
        }

        if (!TryParseJoinType(settings.Join, out var joinType))
        {
            AgentOutput.WriteError("INVALID_JOIN",
                $"Unknown join type: '{settings.Join}'. Supported: miter, round, square",
                format);
            return ExitCodes.InvalidArguments;
        }

        if (!TryParseEndType(settings.End, out var endType))
        {
            AgentOutput.WriteError("INVALID_END",
                $"Unknown end type: '{settings.End}'. Supported: polygon, joined, butt, square, round",
                format);
            return ExitCodes.InvalidArguments;
        }

        if (!PathFillRules.TryParse(settings.FillRule, out var fillRule))
        {
            AgentOutput.WriteError("INVALID_FILL_RULE",
                $"Unknown fill rule: '{settings.FillRule}'. Supported: nonzero, evenodd",
                format);
            return ExitCodes.InvalidArguments;
        }

        var result = _engine.PathStrokeOffset(
            settings.Path,
            settings.Offset,
            joinType,
            endType,
            settings.MiterLimit,
            fillRule,
            ct);

        if (!result.Success)
        {
            AgentOutput.WriteError("PATH_OP_FAILED", result.Error!, format);
            return ExitCodes.GeneralError;
        }

        var output = new
        {
            offset = settings.Offset,
            join = settings.Join.ToLowerInvariant(),
            end = settings.End.ToLowerInvariant(),
            fillRule = PathFillRules.ToName(fillRule),
            pathData = result.PathData
        };

        AgentOutput.WriteSuccess($"Path offset complete (offset={settings.Offset})", format, output);
        return ExitCodes.Success;
    }

    private static bool TryParseJoinType(string value, out OffsetJoinType joinType)
    {
        joinType = value.ToLowerInvariant() switch
        {
            "miter" => OffsetJoinType.Miter,
            "round" => OffsetJoinType.Round,
            "square" => OffsetJoinType.Square,
            _ => (OffsetJoinType)(-1)
        };
        return (int)joinType >= 0;
    }

    private static bool TryParseEndType(string value, out OffsetEndType endType)
    {
        endType = value.ToLowerInvariant() switch
        {
            "polygon" => OffsetEndType.Polygon,
            "joined" => OffsetEndType.Joined,
            "butt" => OffsetEndType.Butt,
            "square" => OffsetEndType.Square,
            "round" => OffsetEndType.Round,
            _ => (OffsetEndType)(-1)
        };
        return (int)endType >= 0;
    }
}

/// <summary>
/// Union multiple paths into one.
/// </summary>
public sealed class PathUnionCommand : Command<PathUnionCommand.Settings>
{
    private readonly IDocumentEngine _engine;

    public PathUnionCommand(IDocumentEngine engine)
    {
        _engine = engine;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<PATHS>")]
        [Description("Paths to union (SVG path d attributes, space-separated)")]
        public required string[] Paths { get; init; }

        [CommandOption("--fill-rule <RULE>")]
        [Description("Fill rule for overlapping contours: nonzero, evenodd (default: nonzero)")]
        [DefaultValue("nonzero")]
        public string FillRule { get; init; } = "nonzero";

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

        if (settings.Paths.Length == 0)
        {
            AgentOutput.WriteError("NO_PATHS", "No paths provided", format);
            return ExitCodes.InvalidArguments;
        }

        if (settings.Paths.Length > 256 || settings.Paths.Sum(path => (long)path.Length) > SurfaceIO.MaxInputFileBytes)
        {
            AgentOutput.WriteError("INPUT_TOO_LARGE", "Too many paths or total path input is too large", format);
            return ExitCodes.InvalidArguments;
        }

        if (!PathFillRules.TryParse(settings.FillRule, out var fillRule))
        {
            AgentOutput.WriteError("INVALID_FILL_RULE",
                $"Unknown fill rule: '{settings.FillRule}'. Supported: nonzero, evenodd",
                format);
            return ExitCodes.InvalidArguments;
        }

        var result = _engine.PathUnionAll(settings.Paths, fillRule, ct);

        if (!result.Success)
        {
            AgentOutput.WriteError("PATH_OP_FAILED", result.Error!, format);
            return ExitCodes.GeneralError;
        }

        var output = new
        {
            inputCount = settings.Paths.Length,
            fillRule = PathFillRules.ToName(fillRule),
            pathData = result.PathData
        };

        AgentOutput.WriteSuccess($"Union of {settings.Paths.Length} paths complete", format, output);
        return ExitCodes.Success;
    }
}
