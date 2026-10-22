using System.Text.Json;
using System.Text.Json.Serialization;
using FTPlus.AgentGraphicDesign.Core;
using ModelContextProtocol.Protocol;

namespace FTPlus.AgentGraphicDesign.Mcp.Tools;

/// <summary>Builds protocol-native MCP results with the shared JSON envelope.</summary>
public static class ToolResult
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static CallToolResult Success(object data) => Create(
        new ToolEnvelope { Ok = true, Data = data },
        isError: false);

    public static CallToolResult Fail(string code, string message, string? path = null, int? operationIndex = null) => Create(
        new ToolEnvelope
        {
            Ok = false,
            Error = new ToolError
            {
                Code = code,
                Message = SurfaceErrorSanitizer.Sanitize(message),
                Path = path is null ? null : SurfaceErrorSanitizer.Sanitize(path),
                OperationIndex = operationIndex
            }
        },
        isError: true);

    /// <summary>
    /// A completed non-success outcome that still carries its result in <c>data</c>: the envelope
    /// is <c>ok:false</c> with both <c>data</c> and <c>error</c>, and the tool result is
    /// <c>IsError=true</c>. Used for the native pixel check drift outcome (a fully decoded output
    /// PNG that differs from a fresh evaluation), never for an execution failure.
    /// </summary>
    public static CallToolResult CompletedFailure(string code, string message, object data, string? path = null) => Create(
        new ToolEnvelope
        {
            Ok = false,
            Data = data,
            Error = new ToolError
            {
                Code = code,
                Message = SurfaceErrorSanitizer.Sanitize(message),
                Path = path is null ? null : SurfaceErrorSanitizer.Sanitize(path)
            }
        },
        isError: true);

    public static CallToolResult FromException(Exception exception, string fallbackCode)
    {
        return exception switch
        {
            DocumentEngineException engineException => Fail(engineException.Code, engineException.Message),
            OperationCanceledException => Fail("CANCELLED", "Operation cancelled"),
            UnauthorizedAccessException => Fail("ACCESS_DENIED", "Access denied"),
            FileNotFoundException => Fail("FILE_NOT_FOUND", exception.Message),
            DirectoryNotFoundException => Fail("DIRECTORY_NOT_FOUND", exception.Message),
            System.Text.Json.JsonException => Fail("SERIALIZATION_ERROR", exception.Message),
            IOException => Fail("IO_ERROR", exception.Message),
            ArgumentException => Fail(EngineErrors.InvalidProperty, exception.Message),
            NotSupportedException => Fail(EngineErrors.InvalidProperty, exception.Message),
            InvalidOperationException => Fail(fallbackCode, exception.Message),
            _ => Fail(fallbackCode, "The operation failed unexpectedly")
        };
    }

    public static bool IsExpectedException(Exception exception) =>
        exception is DocumentEngineException or OperationCanceledException or UnauthorizedAccessException or
            IOException or System.Text.Json.JsonException or ArgumentException or NotSupportedException or
            InvalidOperationException;

    private static CallToolResult Create(ToolEnvelope envelope, bool isError)
    {
        var structured = JsonSerializer.SerializeToElement(envelope, JsonOptions);
        return new CallToolResult
        {
            IsError = isError,
            StructuredContent = structured,
            Content = [new TextContentBlock { Text = structured.GetRawText() }]
        };
    }
}

public sealed class ToolEnvelope
{
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Data { get; init; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ToolError? Error { get; init; }
}

public sealed class ToolError
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
