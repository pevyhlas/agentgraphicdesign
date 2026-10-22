using System.Text.Json;
using System.Text.Json.Serialization;
using FTPlus.AgentGraphicDesign.Core;
using Spectre.Console;

namespace FTPlus.AgentGraphicDesign.Cli.Output;

public enum OutputFormat
{
    Text,
    Json
}

public readonly record struct FormatParseResult(OutputFormat Format, bool Success, string? Error);

public static class OutputFormatExtensions
{
    public static FormatParseResult Parse(string? format)
    {
        return format?.ToLowerInvariant() switch
        {
            "json" => new FormatParseResult(OutputFormat.Json, true, null),
            "text" or null or "" => new FormatParseResult(OutputFormat.Text, true, null),
            _ => new FormatParseResult(default, false, $"Unknown format: '{format}'. Supported: text, json")
        };
    }
}

/// <summary>
/// Unified JSON envelope for machine-readable output.
/// All CLI and MCP responses use this shape.
/// </summary>
public sealed class OutputEnvelope
{
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Data { get; init; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OutputError? Error { get; init; }

    public static OutputEnvelope Success(object data) => new() { Ok = true, Data = data };

    public static OutputEnvelope Fail(string code, string message, string? path = null, int? operationIndex = null) =>
        new()
        {
            Ok = false,
            Error = new OutputError { Code = code, Message = message, Path = path, OperationIndex = operationIndex }
        };
}

public sealed class OutputError
{
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; init; }

    /// <summary>Zero-based index of the failing patch operation, when the error is attributable to one.</summary>
    [JsonPropertyName("operationIndex")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? OperationIndex { get; init; }
}

public static class AgentOutput
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void WriteJson(OutputEnvelope envelope)
    {
        Console.WriteLine(JsonSerializer.Serialize(envelope, JsonOptions));
    }

    public static void WriteData(object data, OutputFormat format)
    {
        if (format == OutputFormat.Json)
        {
            WriteJson(OutputEnvelope.Success(data));
        }
        else
        {
            WriteText(data);
        }
    }

    public static void WriteSuccess(string message, OutputFormat format, object? data = null)
    {
        if (format == OutputFormat.Json)
        {
            WriteJson(OutputEnvelope.Success(data ?? new { message }));
        }
        else
        {
            AnsiConsole.MarkupLine($"[green]✓[/] {message}");
        }
    }

    public static void WriteError(string code, string message, OutputFormat format, string? path = null, int? operationIndex = null)
    {
        message = SurfaceErrorSanitizer.Sanitize(message);
        path = path is null ? null : SurfaceErrorSanitizer.Sanitize(path);

        if (format == OutputFormat.Json)
        {
            WriteJson(OutputEnvelope.Fail(code, message, path, operationIndex));
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]✗[/] [[{code}]] {Markup.Escape(message)}");
            if (operationIndex is int index)
            {
                AnsiConsole.MarkupLine($"    at operation: {index}");
            }
            if (path is not null)
            {
                AnsiConsole.MarkupLine($"    at: {Markup.Escape(path)}");
            }
        }
    }

    /// <summary>
    /// Text-mode rendering of a non-fatal diagnostic. JSON output carries diagnostics
    /// inside the data payload instead, so this writes nothing in JSON mode.
    /// </summary>
    public static void WriteDiagnostic(string code, string message, OutputFormat format)
    {
        if (format == OutputFormat.Json)
        {
            return;
        }

        AnsiConsole.MarkupLine($"[yellow]![/] [[{code}]] {Markup.Escape(SurfaceErrorSanitizer.Sanitize(message))}");
    }

    private static void WriteText<T>(T data)
    {
        Console.WriteLine(JsonSerializer.Serialize(data, JsonOptions));
    }
}

/// <summary>
/// CLI exit codes. Shared across all commands for consistent error handling.
/// </summary>
public static class ExitCodes
{
    public const int Success = 0;
    public const int GeneralError = 1;
    public const int ValidationFailed = 2;
    public const int ValidationWarnings = 3;
    public const int InvalidArguments = 4;

    /// <summary>A lint finding matched <c>--fail-on</c>; lint never fails otherwise.</summary>
    public const int LintFailed = 5;

    /// <summary>Manifest outputs are missing, stale, or unexpected.</summary>
    public const int AssetDrift = 6;
}
