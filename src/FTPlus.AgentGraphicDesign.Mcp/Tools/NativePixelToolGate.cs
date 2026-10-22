using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Wire;
using FTPlus.AgentGraphicDesign.Mcp.Transport;
using ModelContextProtocol.Protocol;

namespace FTPlus.AgentGraphicDesign.Mcp.Tools;

/// <summary>
/// Admission gate for the native pixel MCP tools. Runs inside the call-tool filter before
/// tool dispatch: for a native pixel tool it reads the ORIGINAL argument bytes preserved by
/// <see cref="RawCapturingStdioServerTransport"/> and runs them through the shared
/// <see cref="AgdWireParser"/>. If the capture is missing it fails closed rather than silently
/// weakening validation. Import tools use their own closed shapes and preserve a borrowed
/// slice of the transport capture for operation-owned copying; edit shapes stay unchanged.
/// </summary>
public static class NativePixelToolGate
{
    /// <summary>The reviewed native pixel MCP tool names whose original argument bytes must be validated.</summary>
    public static readonly IReadOnlySet<string> NativePixelTools = new HashSet<string>(StringComparer.Ordinal)
    {
        "create_pixel_asset",
        "inspect_pixel_asset",
        "inspect_pixel_region",
        "edit_pixel_asset",
        "validate_pixel_asset",
        "preview_pixel_asset",
        "export_pixel_asset",
        "check_pixel_export",
        "import_pixel_asset",
        "import_pixel_layer",
    };

    public static bool IsNativePixelTool(string? toolName) =>
        toolName is not null && NativePixelTools.Contains(toolName);

    /// <summary>
    /// Returns <c>null</c> to let the tool proceed, or an error <see cref="CallToolResult"/> to
    /// short-circuit. Non-native tools always proceed.
    /// </summary>
    public static CallToolResult? Admit(string? toolName, IDictionary<string, object?>? contextItems)
    {
        if (!IsNativePixelTool(toolName))
        {
            return null;
        }

        if (contextItems is null ||
            !contextItems.TryGetValue(RawCapturingStdioServerTransport.RawUtf8ItemKey, out var value) ||
            value is not byte[] raw)
        {
            // Fail closed: the required original-byte evidence was not captured before binding.
            return ToolResult.Fail(EngineErrors.InvalidArguments, "Native pixel tool arguments were not captured before binding.");
        }

        try
        {
            if (toolName is "import_pixel_asset" or "import_pixel_layer")
                contextItems[ImportRequestUtf8ItemKey] = AgdWireParser.AdmitImportToolCall(raw, toolName);
            else
                AgdWireParser.AdmitToolCall(raw);
            return null;
        }
        catch (WireFidelityException ex)
        {
            return ToolResult.Fail(ex.Code, ex.Message, ex.Path);
        }
    }

    internal const string ImportRequestUtf8ItemKey = "agd.native-import.original-request-utf8";
}
