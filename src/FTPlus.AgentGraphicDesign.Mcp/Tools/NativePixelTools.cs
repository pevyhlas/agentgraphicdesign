using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Pixels;
using FTPlus.AgentGraphicDesign.Mcp.Transport;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace FTPlus.AgentGraphicDesign.Mcp.Tools;

/// <summary>
/// The native pixel MCP tool surface: the ten <c>*_pixel_*</c> tools over
/// the shared Core <see cref="NativePixelService"/>. Registering them activates the S2 admission gate
/// (<see cref="NativePixelToolGate"/>), which validates the ORIGINAL argument bytes for wire fidelity
/// before typed binding runs. Object-valued arguments (<c>request</c>, <c>rect</c>,
/// <c>expectedReadSnapshot</c>) are consumed from the preserved original capture rather than the
/// SDK-bound dictionary, so duplicate members, the 1 MiB request byte bound and exact numeric spelling
/// (for example <c>revn</c> above 2^53) survive to the shared wire parser. Every path is confined to
/// the server's working directory. Completed non-success outcomes follow the shared envelope
/// conventions: an invalid validation report stays <c>ok:true</c>/<c>data.valid:false</c> and a check
/// drift is <c>ok:false</c> with the comparison still in <c>data</c>; execution failures carry a
/// bounded error and no <c>data</c>.
/// </summary>
[McpServerToolType]
public sealed class NativePixelTools(NativePixelService service, PathConfinement confinement)
{
    [McpServerTool(Name = "import_pixel_asset", Title = "Import Pixel Asset", ReadOnly = false)]
    [Description("Capture, verify and preserve one provided PNG still as a new editable native asset. Trusted quiescent local filesystem; not full admission or provenance approval.")]
    public Task<CallToolResult> ImportPixelAsset(string assetDirectory, string descriptorPath, string name, string layerName,
        CancellationToken cancellationToken = default) => Guarded(async () =>
    {
        // Normalize lexically here; all confinement/type probes and source reads are owned by Core's scope.
        string directory = NativePixelWire.NormalizePath(assetDirectory, confinement.WorkingDirectory, confinement.WorkingDirectory);
        string descriptor = NativePixelWire.NormalizePath(descriptorPath, confinement.WorkingDirectory, confinement.WorkingDirectory);
        return ToolResult.Success(await service.ImportAsync(new(directory, descriptor, name, layerName),
            cancellationToken, confinement.WorkingDirectory));
    }, cancellationToken);

    [McpServerTool(Name = "import_pixel_layer", Title = "Import Pixel Layer", ReadOnly = false)]
    [Description("Append one independent provided PNG still layer under a complete expected snapshot. Preserves original input; same-size canvas; not admission or provenance approval.")]
    public Task<CallToolResult> ImportPixelLayer(
        [Description("Asset directory within the working directory")] string assetDirectory,
        [Description("Closed import request object {descriptorPath, expectedSnapshot, layerName}")] JsonElement request,
        RequestContext<CallToolRequestParams> context, CancellationToken cancellationToken = default) => Guarded(async () =>
    {
        var items = context.JsonRpcRequest?.Context?.Items;
        if (items is null || !items.TryGetValue(RawCapturingStdioServerTransport.RawUtf8ItemKey, out var capture)
            || capture is not byte[] || !items.TryGetValue(NativePixelToolGate.ImportRequestUtf8ItemKey, out var value)
            || value is not ReadOnlyMemory<byte> original)
            throw new NativePixelException(EngineErrors.InvalidArguments, "Original import request was not admitted before binding.");
        return ToolResult.Success(await service.ImportLayerWireAsync(assetDirectory, original,
            confinement.WorkingDirectory, cancellationToken, confinement.WorkingDirectory));
    }, cancellationToken);

    [McpServerTool(Name = "create_pixel_asset", Title = "Create Pixel Asset", ReadOnly = false)]
    [Description("Create a native RGBA8 pixel asset directory with one initial layer. Writes source exclusively.")]
    public Task<CallToolResult> CreatePixelAsset(
        [Description("Asset directory within the working directory")] string assetDirectory,
        [Description("Name for the new pixel asset")] string name,
        [Description("Canvas width in pixels")] int width,
        [Description("Canvas height in pixels")] int height,
        CancellationToken cancellationToken = default)
        => Guarded(async () =>
        {
            var directory = Confine(assetDirectory);
            var result = await service.CreateAsync(
                new CreatePixelAssetRequest(directory, name, width, height), cancellationToken);
            return ToolResult.Success(result);
        }, cancellationToken);

    [McpServerTool(Name = "inspect_pixel_asset", Title = "Inspect Pixel Asset", ReadOnly = true)]
    [Description("Read-only pixel asset metadata plus the validated captured source token. Changes nothing.")]
    public Task<CallToolResult> InspectPixelAsset(
        [Description("Asset directory within the working directory")] string assetDirectory,
        CancellationToken cancellationToken = default)
        => Guarded(async () =>
        {
            var info = await service.InspectAsync(new InspectPixelAssetRequest(Confine(assetDirectory)), cancellationToken);
            return ToolResult.Success(info);
        }, cancellationToken);

    [McpServerTool(Name = "inspect_pixel_region", Title = "Inspect Pixel Region", ReadOnly = true)]
    [Description("Read-only actual bounded RGBA8 or coverage-mask samples for a layer region. Changes nothing.")]
    public Task<CallToolResult> InspectPixelRegion(
        [Description("Asset directory within the working directory")] string assetDirectory,
        [Description("Layer id (GUID) to sample")] string layerId,
        [Description("Sample target: pixels or mask")] string target,
        [Description("Region rectangle object {x,y,width,height} of non-negative integers")] JsonElement rect,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default)
        => Guarded(async () =>
        {
            var directory = Confine(assetDirectory);
            if (!Guid.TryParse(layerId, out var id))
                throw new NativePixelException(EngineErrors.InvalidArguments, "The layer id must be a GUID.");
            var pixelTarget = ParseTarget(target);
            using var arguments = RawArguments.Capture(context);
            var pixelRect = NativePixelWire.Rect(arguments.Require("rect"));
            var result = await service.InspectRegionAsync(
                new InspectPixelRegionRequest(directory, id, pixelTarget, pixelRect), cancellationToken);
            return ToolResult.Success(result);
        }, cancellationToken);

    [McpServerTool(Name = "edit_pixel_asset", Title = "Edit Pixel Asset", ReadOnly = false)]
    [Description("Apply a guarded typed edit batch (expectedSnapshot + operations) with immediate source persistence.")]
    public Task<CallToolResult> EditPixelAsset(
        [Description("Asset directory within the working directory")] string assetDirectory,
        [Description("Edit request object with expectedSnapshot and a 1..64 operations array")] JsonElement request,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default)
        => Guarded(async () =>
        {
            var directory = Confine(assetDirectory);
            using var arguments = RawArguments.Capture(context);
            // Consume the ORIGINAL request bytes (whitespace and exact spelling preserved); relative
            // replacement inputPaths resolve against, and are confined to, the server working directory.
            var editRequest = NativePixelWire.ParseEdit(
                arguments.RawBytes(arguments.Require("request")),
                directory, confinement.WorkingDirectory, confinement.WorkingDirectory);
            // ParseEdit confines nested replace inputPaths lexically to the root; additionally apply
            // the segment-walking reparse check used for every other path, so a symlinked parent
            // directory inside the root cannot alias a file outside it.
            foreach (var operation in editRequest.Operations)
            {
                if (operation is PixelEdit.Replace { Input: RawSampleInput.File file })
                {
                    Confine(file.Path);
                }
            }

            var result = await service.EditAsync(editRequest, cancellationToken);
            return ToolResult.Success(result);
        }, cancellationToken);

    [McpServerTool(Name = "validate_pixel_asset", Title = "Validate Pixel Asset", ReadOnly = true)]
    [Description("Read-only complete native validation. A completed invalid report stays ok:true with data.valid:false.")]
    public Task<CallToolResult> ValidatePixelAsset(
        [Description("Asset directory within the working directory")] string assetDirectory,
        CancellationToken cancellationToken = default)
        => Guarded(async () =>
        {
            var result = await service.ValidateAsync(new ValidatePixelAssetRequest(Confine(assetDirectory)), cancellationToken);
            // A completed invalid report is not an execution error: ok:true, data.valid:false, IsError=false.
            return result switch
            {
                InvalidPixelAssetResult invalid => ToolResult.Success(new { valid = false, errors = invalid.Errors }),
                ValidPixelAssetResult valid => ToolResult.Success(new { valid = true, state = valid.State }),
                _ => ToolResult.Fail(EngineErrors.InvalidArguments, "Unknown validation result.")
            };
        }, cancellationToken);

    [McpServerTool(Name = "preview_pixel_asset", Title = "Preview Pixel Asset", ReadOnly = true)]
    [Description("Read-only native evaluation returned as one inline PNG image block. No output path and no source write.")]
    public Task<CallToolResult> PreviewPixelAsset(
        [Description("Asset directory within the working directory")] string assetDirectory,
        RequestContext<CallToolRequestParams> context,
        [Description("Optional region rectangle object {x,y,width,height}")] JsonElement? rect = null,
        [Description("Optional closed snapshot precondition {assetId,revn,sourceDigest} (at most 1 KiB)")] JsonElement? expectedReadSnapshot = null,
        CancellationToken cancellationToken = default)
        => Guarded(async () =>
        {
            var directory = Confine(assetDirectory);
            using var arguments = RawArguments.Capture(context);
            var pixelRect = arguments.TryGet("rect", out var rectElement) ? NativePixelWire.Rect(rectElement) : (PixelRect?)null;
            var snapshot = ReadSnapshot(arguments);
            var result = await service.PreviewAsync(
                new PreviewPixelAssetRequest(directory, new PixelPreviewDelivery.Inline(), pixelRect, snapshot), cancellationToken);

            // Inline delivery: the image rides one image/png content block; its bytes are JsonIgnore'd
            // on the result, so data carries the bounded facts without repeating the payload.
            var response = ToolResult.Success(result);
            response.Content.Insert(0, ImageContentBlock.FromBytes(result.PngBytes.ToArray(), result.MediaType));
            return response;
        }, cancellationToken);

    [McpServerTool(Name = "export_pixel_asset", Title = "Export Pixel Asset", ReadOnly = false)]
    [Description("Evaluate and publish an export PNG to a confined output path. No source write; existing target needs overwrite.")]
    public Task<CallToolResult> ExportPixelAsset(
        [Description("Asset directory within the working directory")] string assetDirectory,
        [Description("Output PNG path within the working directory")] string outputPath,
        RequestContext<CallToolRequestParams> context,
        [Description("Overwrite an existing output file (default: false)")] bool overwrite = false,
        [Description("Optional closed snapshot precondition {assetId,revn,sourceDigest} (at most 1 KiB)")] JsonElement? expectedReadSnapshot = null,
        CancellationToken cancellationToken = default)
        => Guarded(async () =>
        {
            var directory = Confine(assetDirectory);
            var output = Confine(outputPath);
            using var arguments = RawArguments.Capture(context);
            var result = await service.ExportAsync(
                new ExportPixelAssetRequest(directory, output, overwrite, ReadSnapshot(arguments)), cancellationToken);
            return ToolResult.Success(result);
        }, cancellationToken);

    [McpServerTool(Name = "check_pixel_export", Title = "Check Pixel Export", ReadOnly = true)]
    [Description("Read-only comparison of an existing output PNG against a fresh evaluation. Drift is ok:false with PIXEL_OUTPUT_DRIFT; never repairs.")]
    public Task<CallToolResult> CheckPixelExport(
        [Description("Asset directory within the working directory")] string assetDirectory,
        [Description("Existing output PNG path within the working directory")] string outputPath,
        RequestContext<CallToolRequestParams> context,
        [Description("Optional closed snapshot precondition {assetId,revn,sourceDigest} (at most 1 KiB)")] JsonElement? expectedReadSnapshot = null,
        CancellationToken cancellationToken = default)
        => Guarded(async () =>
        {
            var directory = Confine(assetDirectory);
            var output = Confine(outputPath);
            using var arguments = RawArguments.Capture(context);
            var result = await service.CheckExportAsync(
                new CheckPixelExportRequest(directory, output, ReadSnapshot(arguments)), cancellationToken);

            // A completed mismatch keeps its comparison in data with a typed drift error; a match is plain success.
            return result.Matches
                ? ToolResult.Success(result)
                : ToolResult.CompletedFailure(NativePixelErrors.PixelOutputDrift, "The output PNG differs from a fresh evaluation.", result);
        }, cancellationToken);

    private string Confine(string userPath)
    {
        var confined = confinement.Confine(userPath);
        if (!confined.Success)
            throw new NativePixelException(EngineErrors.PathEscape, confined.Error ?? "Path is outside the working directory.");
        return confined.Path!;
    }

    private static PixelTarget ParseTarget(string target) => target switch
    {
        "pixels" => PixelTarget.Pixels,
        "mask" => PixelTarget.Mask,
        _ => throw new NativePixelException(EngineErrors.InvalidArguments, "The target must be pixels or mask.")
    };

    private static PixelSnapshot? ReadSnapshot(RawArguments arguments) =>
        arguments.TryGet("expectedReadSnapshot", out var element)
            ? NativePixelWire.ParseSnapshot(arguments.RawBytes(element))
            : null;

    /// <summary>
    /// Runs a native tool body, mapping the reviewed <see cref="NativePixelException"/> taxonomy onto
    /// the shared error envelope. Cancellation propagates as <see cref="OperationCanceledException"/>
    /// to the request filter (which maps it to <c>CANCELLED</c>), not caught here.
    /// </summary>
    private static async Task<CallToolResult> Guarded(Func<Task<CallToolResult>> body, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await body();
        }
        catch (NativePixelException ex)
        {
            return ToolResult.Fail(ex.Code, ex.Message, ex.Path, ex.OperationIndex);
        }
    }

    /// <summary>
    /// Holds the parsed ORIGINAL <c>tools/call</c> capture (stashed by
    /// <see cref="RawCapturingStdioServerTransport"/>) so object-valued arguments are read from the
    /// verbatim bytes rather than the SDK's normalized argument dictionary. Fails closed when the
    /// capture is absent, mirroring <see cref="NativePixelToolGate"/>.
    /// </summary>
    private sealed class RawArguments : IDisposable
    {
        private readonly JsonDocument _document;
        private readonly JsonElement _arguments;
        private readonly bool _hasArguments;

        private RawArguments(JsonDocument document, JsonElement arguments, bool hasArguments)
        {
            _document = document;
            _arguments = arguments;
            _hasArguments = hasArguments;
        }

        public static RawArguments Capture(RequestContext<CallToolRequestParams> context)
        {
            var items = context.JsonRpcRequest?.Context?.Items;
            if (items is null ||
                !items.TryGetValue(RawCapturingStdioServerTransport.RawUtf8ItemKey, out var value) ||
                value is not byte[] raw)
            {
                throw new NativePixelException(EngineErrors.InvalidArguments,
                    "Native pixel tool arguments were not captured before binding.");
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(raw);
            }
            catch (JsonException)
            {
                throw new NativePixelException("INVALID_FORMAT", "Malformed native tool-call JSON.");
            }

            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("params", out var parameters) && parameters.ValueKind == JsonValueKind.Object &&
                parameters.TryGetProperty("arguments", out var arguments) && arguments.ValueKind == JsonValueKind.Object)
            {
                return new RawArguments(document, arguments, true);
            }

            return new RawArguments(document, default, false);
        }

        public bool TryGet(string name, out JsonElement element)
        {
            if (_hasArguments && _arguments.TryGetProperty(name, out element))
            {
                return true;
            }

            element = default;
            return false;
        }

        public JsonElement Require(string name) =>
            TryGet(name, out var element)
                ? element
                : throw new NativePixelException(EngineErrors.InvalidArguments, $"The '{name}' argument is required.");

        public byte[] RawBytes(JsonElement element) => JsonMarshal.GetRawUtf8Value(element).ToArray();

        public void Dispose() => _document.Dispose();
    }
}
