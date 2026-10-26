using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Export;
using FTPlus.AgentGraphicDesign.Core.Layout;
using FTPlus.AgentGraphicDesign.Core.Persistence;
using FTPlus.AgentGraphicDesign.Core.Platform;
using FTPlus.AgentGraphicDesign.Core.Rendering;
using FTPlus.AgentGraphicDesign.Core.Serialization;
using FTPlus.AgentGraphicDesign.Skia;
using FTPlus.AgentGraphicDesign.Skia.TextShaping;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace FTPlus.AgentGraphicDesign.Mcp.Tests;

/// <summary>
/// Protocol-level tests that spawn the MCP server and drive initialize → tools/list → tools/call,
/// verifying reflection registration via WithToolsFromAssembly() and stdio transport work correctly.
/// </summary>
public partial class ProtocolTests : IDisposable
{
    private static readonly string McpProjectPath = Path.GetFullPath(
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "src", "FTPlus.AgentGraphicDesign.Mcp"));

    private readonly string _testDir;
    private readonly ITestOutputHelper _output;
    private readonly List<string> _toolCalls = [];
    private Process? _serverProcess;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;
    private int _requestId;

    public ProtocolTests(ITestOutputHelper output)
    {
        _output = output;
        _testDir = Path.Combine(Path.GetTempPath(), $"agd-mcp-protocol-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        StopServer();
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, true); }
            catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task Initialize_ReturnsServerInfo()
    {
        await StartServerAsync();

        var response = await SendRequestAsync("initialize", new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new { name = "test-client", version = "1.0.0" }
        });

        Assert.Equal("2024-11-05", response.RootElement.GetProperty("result").GetProperty("protocolVersion").GetString());
        Assert.Equal("agd-mcp", response.RootElement.GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString());
    }

    [Fact]
    public async Task LateDiscriminators_LoadPatchAndReload_ViaStdio()
    {
        await File.WriteAllTextAsync(Path.Combine(_testDir, "literal.agd.json"), """
            {"version":1,"id":"11111111-1111-1111-1111-111111111111","name":"literal","revn":1,
             "canvas":{"width":128,"height":64},
             "root":{"id":"22222222-2222-2222-2222-222222222222","width":128,"height":64,
               "children":[{"id":"33333333-3333-3333-3333-333333333333","width":16,"height":8,"type":"rectangle"}],"type":"frame"}}
            """);
        await StartServerAsync();
        await InitializeAsync();

        var loaded = await CallToolAsync("load_document", new { idOrPath = "literal.agd.json" });
        Assert.False(loaded.GetProperty("isError").GetBoolean(), loaded.ToString());
        using var operations = JsonDocument.Parse("""
            [{"parentId":"22222222-2222-2222-2222-222222222222",
              "node":{"id":"44444444-4444-4444-4444-444444444444","width":4,"height":4,"type":"ellipse"},
              "op":"addNode"}]
            """);
        var patched = await CallToolAsync("apply_patch", new
        {
            idOrPath = "literal.agd.json",
            expectedRevn = 1,
            operations = operations.RootElement
        });
        Assert.False(patched.GetProperty("isError").GetBoolean(), patched.ToString());
        Assert.Equal(2, ParseToolResult(patched).GetProperty("revn").GetInt64());
        var validated = await CallToolAsync("validate_document", new { idOrPath = "literal.agd.json", strict = true });
        Assert.False(validated.GetProperty("isError").GetBoolean(), validated.ToString());
        Assert.True(ParseToolResult(validated).GetProperty("valid").GetBoolean());
        var nodes = ParseToolResult(await CallToolAsync("list_nodes", new { idOrPath = "literal.agd.json" }));
        Assert.Equal(3, nodes.GetProperty("nodes").GetArrayLength());
    }

    [Fact]
    public async Task ToolsList_AllInputSchemas_HaveNoBooleanSubschemas()
    {
        // The TypeScript MCP SDK (OpenCode, others) rejects boolean subschemas and then drops every
        // tool; a single untyped JsonElement parameter once hid the whole surface from those clients.
        await StartServerAsync();
        await InitializeAsync();

        var tools = (await SendRequestAsync("tools/list", new { })).RootElement.GetProperty("result").GetProperty("tools")
            .EnumerateArray().ToDictionary(tool => tool.GetProperty("name").GetString()!);

        using var inventory = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(RepositoryPaths.Root, "docs", "surface-manifest.json")));
        Assert.Equal(inventory.RootElement.GetProperty("mcpTools").GetArrayLength(), tools.Count);
        foreach (var (name, tool) in tools)
            AssertNoBooleanSubschemas(tool.GetProperty("inputSchema"), $"{name}.inputSchema");
        Assert.Equal(JsonValueKind.Object, tools["import_pixel_layer"].GetProperty("inputSchema")
            .GetProperty("properties").GetProperty("request").ValueKind);
    }

    [Theory]
    [InlineData("""{"properties":{"x":true}}""")]
    [InlineData("""{"properties":{"x":{"anyOf":[{"type":"string"},false]}}}""")]
    [InlineData("""{"properties":{"x":{"type":"array","items":true}}}""")]
    [InlineData("""{"properties":{"x":{"type":"object","additionalProperties":{"not":true}}}}""")]
    public void BooleanSubschemaWalker_RejectsNestedBooleanSchemas(string schema)
    {
        using var document = JsonDocument.Parse(schema);
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => AssertNoBooleanSubschemas(document.RootElement, "synthetic"));
    }

    [Fact]
    public async Task ToolsList_ReturnsAllRegisteredTools()
    {
        await StartServerAsync();
        await InitializeAsync();

        var response = await SendRequestAsync("tools/list", new { });

        var tools = response.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray().ToList();
        var toolNames = tools.Select(t => t.GetProperty("name").GetString()).ToList();

        Assert.Contains("create_document", toolNames);
        Assert.Contains("load_document", toolNames);
        Assert.Contains("list_documents", toolNames);
        Assert.Contains("validate_document", toolNames);
        Assert.Contains("list_nodes", toolNames);
        Assert.Contains("add_node", toolNames);
        Assert.Contains("update_node", toolNames);
        Assert.Contains("delete_node", toolNames);
        Assert.Contains("move_node", toolNames);
        Assert.Contains("reorder_node", toolNames);
        Assert.Contains("export_document", toolNames);
        Assert.Contains("get_document_json", toolNames);
        Assert.Contains("preflight_artifact", toolNames);
        Assert.Contains("verify_artifact", toolNames);
        Assert.Contains("create_pixel_asset", toolNames);
        Assert.Contains("edit_pixel_asset", toolNames);
        Assert.Contains("check_pixel_export", toolNames);
        Assert.Equal(44, toolNames.Count); // 33 vector/artifact tools + 10 native pixel tools + discovery
        using var inventory = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(RepositoryPaths.Root, "docs", "surface-manifest.json")));
        var expected = inventory.RootElement.GetProperty("mcpTools").EnumerateArray().Select(tool => tool.GetProperty("name").GetString()).Order(StringComparer.Ordinal);
        Assert.Equal(expected, toolNames.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Initialize_AdvertisesResourcesCapability()
    {
        await StartServerAsync();

        var response = await SendRequestAsync("initialize", new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new { name = "test-client", version = "1.0.0" }
        });

        var capabilities = response.RootElement.GetProperty("result").GetProperty("capabilities");
        Assert.True(capabilities.TryGetProperty("tools", out _));
        Assert.True(capabilities.TryGetProperty("resources", out _), "server must advertise the resources capability");
    }

    [Fact]
    public async Task ResourcesList_ReturnsTheThreeReferenceResources()
    {
        await StartServerAsync();
        await InitializeAsync();

        var response = await SendRequestAsync("resources/list", new { });

        var resources = response.RootElement.GetProperty("result").GetProperty("resources").EnumerateArray()
            .ToDictionary(resource => resource.GetProperty("uri").GetString()!, StringComparer.Ordinal);
        Assert.Equal(["agd://agent-guide", "agd://format", "agd://schema"], resources.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("application/schema+json", resources["agd://schema"].GetProperty("mimeType").GetString());
        Assert.Equal("text/markdown", resources["agd://format"].GetProperty("mimeType").GetString());
        Assert.Equal("text/markdown", resources["agd://agent-guide"].GetProperty("mimeType").GetString());
        Assert.Equal("agd-agent-guide", resources["agd://agent-guide"].GetProperty("name").GetString());
        Assert.False(string.IsNullOrWhiteSpace(resources["agd://schema"].GetProperty("description").GetString()));
    }

    [Theory]
    [InlineData("agd://schema", "application/schema+json", "schemas/agd-document.schema.json")]
    [InlineData("agd://format", "text/markdown", "docs/format.md")]
    [InlineData("agd://agent-guide", "text/markdown", "docs/agent-guide.md")]
    public async Task ResourcesRead_ReturnsRepositoryFileContent(string uri, string mimeType, string relativeSource)
    {
        var expected = await File.ReadAllTextAsync(Path.Combine(RepositoryPaths.Root, relativeSource.Replace('/', Path.DirectorySeparatorChar)));
        await StartServerAsync();
        await InitializeAsync();

        var response = await SendRequestAsync("resources/read", new { uri });

        Assert.False(response.RootElement.TryGetProperty("error", out var error), error.ToString());
        var content = Assert.Single(response.RootElement.GetProperty("result").GetProperty("contents").EnumerateArray());
        Assert.Equal(uri, content.GetProperty("uri").GetString());
        Assert.Equal(mimeType, content.GetProperty("mimeType").GetString());
        var text = content.GetProperty("text").GetString();
        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.Equal(expected, text);
    }

    [Fact]
    public async Task ResourcesRead_UnknownUri_ReturnsError()
    {
        await StartServerAsync();
        await InitializeAsync();

        var response = await SendRequestAsync("resources/read", new { uri = "agd://missing" });

        Assert.True(response.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task ToolsCall_CreateDocument_WithTemplate_UsesCatalogCanvas()
    {
        await StartServerAsync();
        await InitializeAsync();

        var response = await CallToolAsync("create_document", new { name = "templated-icon", template = "web-icon-24" });

        Assert.False(response.GetProperty("isError").GetBoolean());
        var data = ParseToolResult(response);
        Assert.Equal("web-icon-24", data.GetProperty("template").GetString());
        Assert.Equal(24, data.GetProperty("canvas").GetProperty("width").GetDouble());
        Assert.Equal(24, data.GetProperty("canvas").GetProperty("height").GetDouble());

        var validate = ParseToolResult(await CallToolAsync("validate_document", new
        {
            idOrPath = data.GetProperty("id").GetString(),
            profile = "web-icon",
            strict = true
        }));
        Assert.True(validate.GetProperty("valid").GetBoolean());
    }

    [Fact]
    public async Task ToolsCall_CreateDocument_UnknownTemplate_ReturnsInvalidArguments()
    {
        await StartServerAsync();
        await InitializeAsync();

        var response = await CallToolAsync("create_document", new { name = "bad", template = "poster" });

        Assert.True(response.GetProperty("isError").GetBoolean());
        var error = response.GetProperty("structuredContent").GetProperty("error");
        Assert.Equal("INVALID_ARGUMENTS", error.GetProperty("code").GetString());
        Assert.Contains("web-icon-32", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task ToolsCall_ListDocuments_ReturnsDocumentArray()
    {
        await StartServerAsync();
        await InitializeAsync();

        await CallToolAsync("create_document", new { name = "list-test-1", width = 100, height = 100 });
        await CallToolAsync("create_document", new { name = "list-test-2", width = 100, height = 100 });

        var listResponse = await CallToolAsync("list_documents", new { });
        var listResult = ParseToolResult(listResponse);

        Assert.True(listResult.TryGetProperty("documents", out var docs));
        var docList = docs.EnumerateArray().ToList();
        Assert.True(docList.Count >= 2);

        var names = docList.Select(d => d.GetProperty("name").GetString()).ToList();
        Assert.Contains("list-test-1", names);
        Assert.Contains("list-test-2", names);
    }

    [Fact]
    public async Task ToolsList_ToolsHaveCorrectSchemas()
    {
        await StartServerAsync();
        await InitializeAsync();

        var response = await SendRequestAsync("tools/list", new { });

        var tools = response.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray().ToList();
        var createDocTool = tools.First(t => t.GetProperty("name").GetString() == "create_document");

        Assert.True(createDocTool.TryGetProperty("description", out _));
        Assert.True(createDocTool.TryGetProperty("inputSchema", out var inputSchema));

        var properties = inputSchema.GetProperty("properties");
        Assert.True(properties.TryGetProperty("name", out _));
        Assert.True(properties.TryGetProperty("width", out _));
        Assert.True(properties.TryGetProperty("height", out _));

        Assert.True(properties.TryGetProperty("template", out _));

        var validateTool = tools.First(t => t.GetProperty("name").GetString() == "validate_document");
        Assert.True(validateTool.GetProperty("inputSchema").GetProperty("properties")
            .TryGetProperty("profile", out _));
        var exportTool = tools.First(t => t.GetProperty("name").GetString() == "export_document");
        Assert.True(exportTool.GetProperty("inputSchema").GetProperty("properties")
            .TryGetProperty("profile", out _));

        var preflightTool = tools.Single(t => t.GetProperty("name").GetString() == "preflight_artifact");
        Assert.Equal("Preflight Artifact", preflightTool.GetProperty("title").GetString());
        Assert.True(preflightTool.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean());
        var preflightSchema = preflightTool.GetProperty("inputSchema");
        var descriptor = Assert.Single(preflightSchema.GetProperty("properties").EnumerateObject());
        Assert.Equal("descriptorPath", descriptor.Name);
        Assert.Equal("string", descriptor.Value.GetProperty("type").GetString());
        Assert.Equal("descriptorPath", Assert.Single(preflightSchema.GetProperty("required").EnumerateArray()).GetString());

        var verifyTool = tools.Single(t => t.GetProperty("name").GetString() == "verify_artifact");
        Assert.Equal("Verify Artifact", verifyTool.GetProperty("title").GetString());
        Assert.True(verifyTool.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean());
        Assert.Contains("managed-in-process", verifyTool.GetProperty("description").GetString());
        var verifySchema = verifyTool.GetProperty("inputSchema");
        var verifyDescriptor = Assert.Single(verifySchema.GetProperty("properties").EnumerateObject());
        Assert.Equal("descriptorPath", verifyDescriptor.Name);
        Assert.Equal("string", verifyDescriptor.Value.GetProperty("type").GetString());
        Assert.Equal("descriptorPath", Assert.Single(verifySchema.GetProperty("required").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task ToolsCall_PreflightArtifact_ColdStartAndCallsAreReadOnlyAndStoreless()
    {
        var descriptor = ArtifactTestBundle.Create(_testDir);
        // Capture before process startup, not just before the tool call: eager store
        // initialization would create a root .agd-store.lock even for read-only tools.
        var snapshot = ArtifactTestBundle.Snapshot(_testDir);
        await StartServerAsync();
        await InitializeAsync();
        await SendRequestAsync("tools/list", new { });
        Assert.Equal(snapshot, ArtifactTestBundle.Snapshot(_testDir));

        var success = await CallToolAsync("preflight_artifact", new
        {
            descriptorPath = Path.GetRelativePath(_testDir, descriptor)
        });
        Assert.False(success.GetProperty("isError").GetBoolean());
        ArtifactTestBundle.AssertSuccess(PreflightEnvelope(success), descriptor, 1);
        Assert.Equal(snapshot, ArtifactTestBundle.Snapshot(_testDir));

        var escaped = await CallToolAsync("preflight_artifact", new { descriptorPath = "../private-preflight-input.json" });
        Assert.True(escaped.GetProperty("isError").GetBoolean());
        ArtifactTestBundle.AssertError(PreflightEnvelope(escaped), "PATH_ESCAPE");
        Assert.Equal(snapshot, ArtifactTestBundle.Snapshot(_testDir));

        Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(descriptor)!, ".private-preflight-input"));
        snapshot = ArtifactTestBundle.Snapshot(_testDir);
        var failure = await CallToolAsync("preflight_artifact", new { descriptorPath = descriptor });
        Assert.True(failure.GetProperty("isError").GetBoolean());
        ArtifactTestBundle.AssertError(PreflightEnvelope(failure), "ARTIFACT_INVENTORY_MISMATCH");
        Assert.Equal(snapshot, ArtifactTestBundle.Snapshot(_testDir));
        Assert.False(File.Exists(Path.Combine(_testDir, ".agd-store.lock")));
        Assert.False(Directory.Exists(Path.Combine(_testDir, ".agd")));
        StopServer();
        Assert.Equal(snapshot, ArtifactTestBundle.Snapshot(_testDir));

        static JsonElement PreflightEnvelope(JsonElement result)
        {
            var structured = result.GetProperty("structuredContent");
            var content = Assert.Single(result.GetProperty("content").EnumerateArray());
            Assert.Equal("text", content.GetProperty("type").GetString());
            Assert.Equal(structured.GetRawText(), content.GetProperty("text").GetString());
            return structured;
        }
    }

    [Fact]
    public async Task ToolsCall_VerifyArtifact_ColdStartAndCallsAreReadOnlyAndStoreless()
    {
        var descriptor = ArtifactTestBundle.Create(_testDir);
        var snapshot = ArtifactTestBundle.Snapshot(_testDir);
        await StartServerAsync();
        await InitializeAsync();
        await SendRequestAsync("tools/list", new { });
        Assert.Equal(snapshot, ArtifactTestBundle.Snapshot(_testDir));

        var success = await CallToolAsync("verify_artifact", new
        {
            descriptorPath = Path.GetRelativePath(_testDir, descriptor)
        });
        Assert.False(success.GetProperty("isError").GetBoolean());
        ArtifactVerificationTestBundle.AssertSuccess(VerificationEnvelope(success), descriptor);
        Assert.Equal(snapshot, ArtifactTestBundle.Snapshot(_testDir));

        var escaped = await CallToolAsync("verify_artifact", new { descriptorPath = "../private-preflight-input.json" });
        Assert.True(escaped.GetProperty("isError").GetBoolean());
        ArtifactVerificationTestBundle.AssertError(VerificationEnvelope(escaped), "PATH_ESCAPE");
        Assert.DoesNotContain(_testDir, escaped.GetRawText());
        Assert.Equal(snapshot, ArtifactTestBundle.Snapshot(_testDir));

        // The first file decodes, but the second has an invalid DEFLATE block with matching CRC/hash.
        ArtifactVerificationTestBundle.AddSecondFile(descriptor, ArtifactVerificationTestBundle.PngWithIdat("78010700000000"));
        snapshot = ArtifactTestBundle.Snapshot(_testDir);
        var preflight = await CallToolAsync("preflight_artifact", new { descriptorPath = descriptor });
        Assert.False(preflight.GetProperty("isError").GetBoolean());
        var structural = VerificationEnvelope(preflight);
        Assert.True(structural.GetProperty("ok").GetBoolean());
        Assert.Equal("encoded-integrity-and-png-structure", structural.GetProperty("data").GetProperty("verification").GetString());
        Assert.False(structural.GetProperty("data").GetProperty("pixelsDecoded").GetBoolean());
        Assert.Equal(2, structural.GetProperty("data").GetProperty("fileCount").GetInt32());
        Assert.Equal(snapshot, ArtifactTestBundle.Snapshot(_testDir));

        var failure = await CallToolAsync("verify_artifact", new { descriptorPath = descriptor });
        Assert.True(failure.GetProperty("isError").GetBoolean());
        var failedEnvelope = VerificationEnvelope(failure);
        ArtifactVerificationTestBundle.AssertError(failedEnvelope, "ARTIFACT_PNG_INVALID");
        Assert.Contains("$.files[1]", failedEnvelope.GetProperty("error").GetProperty("message").GetString());
        Assert.DoesNotContain(_testDir, failure.GetRawText());
        Assert.Equal(snapshot, ArtifactTestBundle.Snapshot(_testDir));

        var metadata = JsonNode.Parse(File.ReadAllText(descriptor))!;
        metadata["metadataHash"] = new string('0', 64);
        File.WriteAllText(descriptor, metadata.ToJsonString());
        snapshot = ArtifactTestBundle.Snapshot(_testDir);
        var invalidMetadata = await CallToolAsync("verify_artifact", new { descriptorPath = descriptor });
        Assert.True(invalidMetadata.GetProperty("isError").GetBoolean());
        var metadataEnvelope = VerificationEnvelope(invalidMetadata);
        ArtifactVerificationTestBundle.AssertError(metadataEnvelope, "PARSE_ERROR");
        Assert.Contains("$", metadataEnvelope.GetProperty("error").GetProperty("message").GetString());
        Assert.DoesNotContain(_testDir, invalidMetadata.GetRawText());
        Assert.Equal(snapshot, ArtifactTestBundle.Snapshot(_testDir));
        Assert.False(File.Exists(Path.Combine(_testDir, ".agd-store.lock")));
        Assert.False(Directory.Exists(Path.Combine(_testDir, ".agd")));
        StopServer();
        Assert.Equal(snapshot, ArtifactTestBundle.Snapshot(_testDir));

        static JsonElement VerificationEnvelope(JsonElement result)
        {
            var structured = result.GetProperty("structuredContent");
            var content = Assert.Single(result.GetProperty("content").EnumerateArray());
            Assert.Equal("text", content.GetProperty("type").GetString());
            Assert.Equal(structured.GetRawText(), content.GetProperty("text").GetString());
            return structured;
        }
    }

    [Fact]
    public async Task ToolsCall_CreateDocument_ReturnsDocumentInfo()
    {
        await StartServerAsync();
        await InitializeAsync();

        var response = await CallToolAsync("create_document", new
        {
            name = "protocol-test",
            width = 800,
            height = 600
        });

        var data = ParseToolResult(response);

        Assert.False(response.GetProperty("isError").GetBoolean());
        Assert.True(response.TryGetProperty("structuredContent", out _));
        Assert.True(data.TryGetProperty("id", out _));
        Assert.Equal("protocol-test", data.GetProperty("name").GetString());
    }

    [Fact]
    public async Task ToolsCall_BusinessError_SetsProtocolIsErrorAndStructuredEnvelope()
    {
        await StartServerAsync();
        await InitializeAsync();

        var response = await CallToolAsync("load_document", new { idOrPath = Guid.NewGuid().ToString() });

        Assert.True(response.GetProperty("isError").GetBoolean());
        var structured = response.GetProperty("structuredContent");
        Assert.False(structured.GetProperty("ok").GetBoolean());
        Assert.Equal("DOCUMENT_NOT_FOUND", structured.GetProperty("error").GetProperty("code").GetString());
        var text = response.GetProperty("content").EnumerateArray().Single().GetProperty("text").GetString();
        Assert.DoesNotContain(_testDir, text);
    }

    [Fact]
    public async Task ToolsCall_ExportTokensIoFailure_ReturnsStructuredErrorEnvelope()
    {
        await StartServerAsync();
        await InitializeAsync();
        var create = await CallToolAsync("create_document", new { name = "token-io", width = 100, height = 100 });
        var id = ParseToolResult(create).GetProperty("id").GetString()!;

        var response = await CallToolAsync("export_dtcg_tokens", new { idOrPath = id, output = "." });

        Assert.True(response.GetProperty("isError").GetBoolean());
        var structured = response.GetProperty("structuredContent");
        Assert.False(structured.GetProperty("ok").GetBoolean());
        var code = structured.GetProperty("error").GetProperty("code").GetString();
        Assert.Contains(code, new[] { "ACCESS_DENIED", "IO_ERROR" });
        var text = response.GetProperty("content").EnumerateArray().Single().GetProperty("text").GetString();
        Assert.Equal(structured.GetRawText(), text);
    }

    [Fact]
    public async Task ToolsCall_InvalidTool_ReturnsError()
    {
        await StartServerAsync();
        await InitializeAsync();

        var response = await SendRequestAsync("tools/call", new
        {
            name = "NonExistentTool",
            arguments = new { }
        });

        Assert.True(response.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task EndToEnd_CreateEditValidateExport_ViaProtocol()
    {
        await StartServerAsync();
        await InitializeAsync();

        var createResponse = await CallToolAsync("create_document", new
        {
            name = "e2e-protocol",
            width = 800,
            height = 600,
            background = "#ffffff"
        });
        var createResult = ParseToolResult(createResponse);
        var docId = createResult.GetProperty("id").GetString()!;

        var addResponse = await CallToolAsync("add_node", new
        {
            idOrPath = docId,
            parentId = "root",
            type = "rectangle",
            name = "test-rect",
            width = 100,
            height = 50,
            x = 50,
            y = 50,
            fill = "#ff0000"
        });
        var addResult = ParseToolResult(addResponse);
        Assert.True(addResult.TryGetProperty("nodeId", out _));

        var validateResponse = await CallToolAsync("validate_document", new { idOrPath = docId });
        var validateResult = ParseToolResult(validateResponse);
        Assert.True(validateResult.GetProperty("valid").GetBoolean());

        var listResponse = await CallToolAsync("list_nodes", new { idOrPath = docId });
        var listResult = ParseToolResult(listResponse);
        var nodes = listResult.GetProperty("nodes").EnumerateArray().ToList();
        Assert.Equal(2, nodes.Count);

        var exportPath = Path.Combine(_testDir, "output.svg");
        var exportResponse = await CallToolAsync("export_document", new
        {
            idOrPath = docId,
            outputPath = "output.svg"
        });
        var exportResult = ParseToolResult(exportResponse);
        Assert.True(exportResult.TryGetProperty("path", out _));
        Assert.True(File.Exists(exportPath));

        var svg = await File.ReadAllTextAsync(exportPath);
        Assert.Contains("<svg", svg);
        Assert.Contains("<rect", svg);
    }

    [Fact]
    public async Task Roadmap02_ThreeWrappedCards_AlignDistributeAndLint_InFiveToolCalls_ViaStdio()
    {
        await StartServerAsync();
        await InitializeAsync();

        // Client-known IDs remove any need for list_nodes/get_document_json discovery calls.
        static string NodeId(int card, int child = 0) => $"00000000-0000-0000-0000-{card * 100 + child:D12}";
        var cards = new[]
        {
            (X: 32, Y: 72, Color: "#ffffff", Title: "Build a clear visual language",
                Body: "Give every idea room to breathe with readable type, generous spacing, and a consistent rhythm across the page."),
            (X: 330, Y: 32, Color: "#eef2ff", Title: "Keep every card on the same grid",
                Body: "Align the cards by their visible bounds, then distribute the columns so the spaces between them stay equal."),
            (X: 668, Y: 96, Color: "#ecfdf5", Title: "Ship assets with useful feedback",
                Body: "Check contrast and canvas bounds before handing off the layout. Keep the source editable for the next revision.")
        };
        var recipePath = Path.GetFullPath(Path.Combine(McpProjectPath, "..", "..", "docs", "mcp-card-recipe.md"));
        var recipe = await File.ReadAllTextAsync(recipePath);
        var inputs = Regex.Matches(recipe, @"```json\r?\n(?<json>.*?)\r?\n```", RegexOptions.Singleline)
            .Select(match => JsonSerializer.Deserialize<JsonElement>(match.Groups["json"].Value)).ToArray();
        Assert.Equal(6, inputs.Length);
        Assert.Equal(3, inputs[2].GetArrayLength());

        // Execute the documented payloads, substituting only the ID and JSON-string transport.
        Dictionary<string, JsonElement> RecipeArguments(int index, string documentId)
        {
            var arguments = inputs[index].Deserialize<Dictionary<string, JsonElement>>()!;
            Assert.Equal("<DOCUMENT_ID>", arguments["idOrPath"].GetString());
            arguments["idOrPath"] = JsonSerializer.SerializeToElement(documentId);
            if (index == 1)
                arguments["operations"] = inputs[2].Clone();
            return arguments;
        }

        var createResponse = await CallToolAsync("create_document", inputs[0]);
        Assert.False(createResponse.GetProperty("isError").GetBoolean());
        var docId = ParseToolResult(createResponse).GetProperty("id").GetString()!;

        var patchResponse = await CallToolAsync("apply_patch", RecipeArguments(1, docId));
        Assert.False(patchResponse.GetProperty("isError").GetBoolean(), patchResponse.ToString());
        var patch = ParseToolResult(patchResponse);
        Assert.Equal(3, patch.GetProperty("applied").GetInt32());
        Assert.Equal(2, patch.GetProperty("revn").GetInt64());
        Assert.Empty(patch.GetProperty("warnings").EnumerateArray());

        var alignResponse = await CallToolAsync("align_nodes", RecipeArguments(3, docId));
        Assert.False(alignResponse.GetProperty("isError").GetBoolean(), alignResponse.ToString());
        var aligned = ParseToolResult(alignResponse);
        Assert.Equal(3, aligned.GetProperty("documentRevn").GetInt64());
        Assert.Equal(32, aligned.GetProperty("reference").GetProperty("y").GetDouble());
        Assert.Equal(3, aligned.GetProperty("nodes").GetArrayLength());
        for (var index = 0; index < cards.Length; index++)
        {
            var placement = aligned.GetProperty("nodes").EnumerateArray()
                .Single(node => node.GetProperty("id").GetString() == NodeId(index + 1));
            Assert.Equal(cards[index].Y, placement.GetProperty("previousY").GetDouble());
            Assert.Equal(cards[index].X, placement.GetProperty("x").GetDouble());
            Assert.Equal(32, placement.GetProperty("y").GetDouble());
        }

        var distributeResponse = await CallToolAsync("distribute_nodes", RecipeArguments(4, docId));
        Assert.False(distributeResponse.GetProperty("isError").GetBoolean(), distributeResponse.ToString());
        var distributed = ParseToolResult(distributeResponse);
        Assert.Equal(4, distributed.GetProperty("documentRevn").GetInt64());
        Assert.Equal(58, distributed.GetProperty("gap").GetDouble());
        Assert.Equal(3, distributed.GetProperty("nodes").GetArrayLength());
        for (var index = 0; index < cards.Length; index++)
        {
            var placement = distributed.GetProperty("nodes").EnumerateArray()
                .Single(node => node.GetProperty("id").GetString() == NodeId(index + 1));
            Assert.Equal(cards[index].X, placement.GetProperty("previousX").GetDouble());
            Assert.Equal(32 + index * 318, placement.GetProperty("x").GetDouble());
            Assert.Equal(32, placement.GetProperty("y").GetDouble());
        }

        var lintResponse = await CallToolAsync("lint_document", RecipeArguments(5, docId));
        Assert.False(lintResponse.GetProperty("isError").GetBoolean(), lintResponse.ToString());
        var lint = ParseToolResult(lintResponse);
        Assert.Equal("default", lint.GetProperty("profile").GetString());
        Assert.False(lint.GetProperty("gridChecked").GetBoolean());
        Assert.Equal(0, lint.GetProperty("warningCount").GetInt32());
        Assert.Equal(0, lint.GetProperty("infoCount").GetInt32());
        Assert.False(lint.GetProperty("truncated").GetBoolean());
        Assert.Empty(lint.GetProperty("diagnostics").EnumerateArray());

        string[] expectedCalls = ["create_document", "apply_patch", "align_nodes", "distribute_nodes", "lint_document"];
        Assert.Equal(5, _toolCalls.Count);
        Assert.Equal(expectedCalls, _toolCalls);
        Assert.Equal(6, _requestId); // Five tools/call requests plus initialize; notifications have no ID.
        _output.WriteLine($"Authoring tools/call requests ({_toolCalls.Count}): {string.Join(" -> ", _toolCalls)}");
        _output.WriteLine("initialize + notifications/initialized are transport setup, not tools/call requests. No discovery, retries, or export tool calls.");

        // Read-only harness verification is outside the authoring budget; never call a sixth tool.
        var savedPath = Assert.Single(Directory.GetFiles(_testDir, "*.agd.json"));
        var savedJson = await File.ReadAllTextAsync(savedPath);
        var document = DocumentSerializer.Deserialize(savedJson)!;
        Assert.Equal(Guid.Parse(docId), document.Id);
        Assert.Equal(4, document.Revn);
        Assert.Equal("#0f172a", document.Canvas.Background);
        Assert.Null(document.Root.Style);
        Assert.Equal(3, document.Root.Children.Count);

        using var measurer = new SkiaTextMeasurer();
        var engine = new DocumentEngine(new FileDocumentStore(_testDir), textMeasurer: measurer);
        var bounds = engine.GetBounds(document).ToDictionary(node => node.Id);
        Assert.Equal(13, bounds.Count);
        Assert.All(bounds.Values, node =>
        {
            Assert.True(node.Visible);
            Assert.False(node.Clipped);
            Assert.Empty(node.Diagnostics);
        });
        var worldCards = Enumerable.Range(1, 3).Select(card => bounds[Guid.Parse(NodeId(card))].World!.Value).ToArray();
        Assert.Equal(58, worldCards[1].X - worldCards[0].Right);
        Assert.Equal(worldCards[1].X - worldCards[0].Right, worldCards[2].X - worldCards[1].Right);

        using var fonts = new TextShapingService(new FontResolutionOptions { SystemFonts = false, StrictFonts = true });
        var svg = await new SvgExportAdapter(measurer).ExportAsync(document, new ExportOptions());
        var xml = XDocument.Parse(Encoding.UTF8.GetString(svg.Data));
        XNamespace ns = "http://www.w3.org/2000/svg";
        Assert.Equal(6, xml.Descendants(ns + "text").Count());
        for (var index = 0; index < cards.Length; index++)
        {
            var frame = Assert.IsType<FrameNode>(document.Root.Children.Single(node => node.Id == Guid.Parse(NodeId(index + 1))));
            Assert.Null(frame.Style); // Containers do not paint backgrounds or introduce group opacity.
            Assert.False(frame.ClipContent);
            Assert.Equal(260, frame.Width);
            Assert.Equal(280, frame.Height);
            Assert.Equal(new BoundingBox(32 + index * 318, 32, 260, 280), worldCards[index]);
            var children = frame.Children.OrderBy(node => node.Order, StringComparer.Ordinal).ToArray();
            Assert.Equal(3, children.Length);
            var background = Assert.IsType<RectangleNode>(children[0]);
            Assert.Equal(Guid.Parse(NodeId(index + 1, 1)), background.Id);
            Assert.Equal(cards[index].Color, background.Style!.Fill!.Color);
            Assert.Null(background.Style.Fill.Gradient);
            Assert.Equal(1, background.Style.Opacity);
            Assert.Equal(worldCards[index], bounds[background.Id].World);
            var svgBackground = xml.Descendants(ns + "rect").Single(node => (string?)node.Attribute("data-name") == background.Name);
            Assert.Equal(cards[index].Color, (string?)svgBackground.Attribute("fill"));

            for (var child = 1; child <= 2; child++)
            {
                var text = Assert.IsType<TextNode>(children[child]);
                Assert.Equal(Guid.Parse(NodeId(index + 1, child + 1)), text.Id);
                Assert.Equal(child == 1 ? cards[index].Title : cards[index].Body, text.Text);
                Assert.DoesNotContain("\n", text.Text);
                Assert.DoesNotContain("\r", text.Text);
                Assert.Equal(BundledFonts.FamilyName, text.FontFamily);
                var font = fonts.ResolveFont(text.FontFamily, text.FontWeight);
                Assert.Equal(FontSource.Bundled, font.Source);
                Assert.False(font.Substituted);
                Assert.Equal(212, text.MaxWidth);
                Assert.Equal("#0f172a", text.Style!.Fill!.Color);
                Assert.Null(text.Style.Fill.Gradient);
                Assert.Equal(1, text.Style.Opacity);

                var textBounds = bounds[text.Id];
                Assert.True(textBounds.Measured);
                var world = textBounds.World!.Value;
                Assert.Equal(worldCards[index].X + 24, world.X);
                Assert.InRange(world.Right, world.X, worldCards[index].Right - 24);
                Assert.InRange(world.Y, worldCards[index].Y + 24, worldCards[index].Bottom - 24);
                Assert.InRange(world.Bottom, world.Y, worldCards[index].Bottom - 24);
                var style = TextLayout.Resolve(text, null);
                var fullLine = measurer.Measure(style.ToMeasurementRequest(text.Text));
                Assert.True(fullLine.Measured);
                Assert.True(fullLine.Width > text.MaxWidth, "The source must require wrapping, not merely enable maxWidth.");
                var svgText = xml.Descendants(ns + "text").Single(node => (string?)node.Attribute("data-name") == text.Name);
                Assert.Equal("Inter", (string?)svgText.Attribute("font-family"));
                var lines = svgText.Elements(ns + "tspan").ToArray();
                Assert.True(lines.Length > 1, $"{text.Name} must export multiple wrapped lines.");
                Assert.Equal(text.Text, string.Join(" ", lines.Select(line => line.Value)));
                for (var line = 0; line < lines.Length; line++)
                {
                    Assert.Equal(0, (double)lines[line].Attribute("x")!);
                    Assert.Equal(line == 0 ? 0 : text.FontSize * style.LineHeight, (double)lines[line].Attribute("dy")!);
                    Assert.InRange(measurer.Measure(style.ToMeasurementRequest(lines[line].Value)).Width, 1, 212);
                }
                Assert.Equal(worldCards[index].Y + text.Transform!.Y + text.FontSize - fullLine.Ascent, world.Y, 4);
                Assert.Equal((lines.Length - 1) * text.FontSize * style.LineHeight + fullLine.LineHeight, world.Height, 4);
                _output.WriteLine($"{text.Name}: {lines.Length} wrapped SVG lines, measured with bundled {font.FamilyName}.");
            }
            Assert.True(bounds[children[1].Id].World!.Value.Bottom <= bounds[children[2].Id].World!.Value.Y);
        }
        Assert.Empty(fonts.Diagnostics);

        var png = new SkiaRenderer().Render(document, new RenderOptions { SystemFonts = false, StrictFonts = true });
        Assert.Equal("image/png", png.MimeType);
        Assert.Equal(960, png.Width);
        Assert.Equal(400, png.Height);
        Assert.Empty(png.Diagnostics);
        using var bitmap = SKBitmap.Decode(png.Data);
        Assert.NotNull(bitmap);
        Assert.Equal(SKColor.Parse("#0f172a"), bitmap.GetPixel(310, 40));
        for (var index = 0; index < cards.Length; index++)
            Assert.Equal(SKColor.Parse(cards[index].Color), bitmap.GetPixel(40 + index * 318, 40));

        var artifactDir = Path.Combine(AppContext.BaseDirectory, "artifacts", "mcp-card-recipe");
        Directory.CreateDirectory(artifactDir);
        await File.WriteAllTextAsync(Path.Combine(artifactDir, "cards.agd.json"), savedJson);
        await File.WriteAllBytesAsync(Path.Combine(artifactDir, "cards.svg"), svg.Data);
        await File.WriteAllBytesAsync(Path.Combine(artifactDir, "cards.png"), png.Data);
        Assert.Equal(savedJson, await File.ReadAllTextAsync(savedPath));
        Assert.Equal(5, _toolCalls.Count);
        Assert.Equal(expectedCalls, _toolCalls);
        _output.WriteLine("World card bounds: (32,32,260,280), (350,32,260,280), (668,32,260,280); both gaps 58 px. Lint: 0 warnings, 0 infos.");
        _output.WriteLine($"Harness-only artifacts: {artifactDir}. Structural/render checks are not human visual approval.");
    }

    [Fact]
    public async Task WorkingDirectory_ConfinesPathsCorrectly()
    {
        await StartServerAsync();
        await InitializeAsync();

        var createResponse = await CallToolAsync("create_document", new { name = "confined-test", width = 800, height = 600 });
        var docId = ParseToolResult(createResponse).GetProperty("id").GetString()!;

        var exportResponse = await CallToolAsync("export_document", new
        {
            idOrPath = docId,
            outputPath = "../../../escape.svg"
        });
        var exportResult = ParseToolResult(exportResponse);
        Assert.True(exportResponse.GetProperty("isError").GetBoolean());
        Assert.True(exportResult.TryGetProperty("error", out var error));
        Assert.Equal("PATH_ESCAPE", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Readiness_VectorJourney_UsesTypedBatchesBoundedInspectionAndThumbnail()
    {
        await StartServerAsync();
        await InitializeAsync();
        var capabilities = NativeData(await CallToolAsync("get_capabilities", new { }));
        Assert.Equal(_testDir, capabilities.GetProperty("workingDirectory").GetString());
        Assert.True(capabilities.GetProperty("domains").GetProperty("layeredStillPixels").GetBoolean());
        Assert.False(capabilities.GetProperty("domains").GetProperty("providedArtifactAdmission").GetBoolean());
        Assert.Equal(PlatformProfile.Current.ToCapabilitiesJson().ToJsonString(),
            JsonNode.Parse(capabilities.GetProperty("platform").GetRawText())!.ToJsonString());
        Assert.False(capabilities.TryGetProperty("supportedPlatform", out _));
        Assert.False(capabilities.TryGetProperty("currentPlatformMatches", out _));
        Assert.StartsWith("0.3.0", capabilities.GetProperty("version").GetString());
        foreach (var format in new[] { "vector", "nativePixels", "providedArtifact" })
            Assert.Equal(1, capabilities.GetProperty("formats").GetProperty(format).GetInt32());
        Assert.True(capabilities.GetProperty("domains").GetProperty("nativeImageImport").GetBoolean());
        Assert.Equal("agd.pixel-still-import.managed-local.v1", capabilities.GetProperty("nativeStillImport").GetProperty("profile").GetString());
        foreach (var unavailable in new[] { "animation", "tiles", "blenderExecution" })
            Assert.False(capabilities.GetProperty("domains").GetProperty(unavailable).GetBoolean());
        Assert.Equal(new[] { "assets build", "assets check", "sprite" }, capabilities.GetProperty("cliOnly").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(new[] { "agd://schema", "agd://format", "agd://agent-guide" }, capabilities.GetProperty("resources").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(500, capabilities.GetProperty("limits").GetProperty("patchOperations").GetInt32());
        Assert.Equal(8 * 1024 * 1024, capabilities.GetProperty("limits").GetProperty("stdioMessageBytes").GetInt32());
        Assert.Equal(1000, capabilities.GetProperty("limits").GetProperty("nodePage").GetInt32());
        Assert.Equal(100_000, capabilities.GetProperty("limits").GetProperty("nodeScan").GetInt32());
        Assert.Equal(512, capabilities.GetProperty("limits").GetProperty("defaultPreviewLongestEdge").GetInt32());
        Assert.Equal(2048, capabilities.GetProperty("limits").GetProperty("explicitPreviewDimension").GetInt32());

        var schemas = (await SendRequestAsync("tools/list", new { })).RootElement.GetProperty("result").GetProperty("tools")
            .EnumerateArray().ToDictionary(tool => tool.GetProperty("name").GetString()!);
        Assert.Equal("array", schemas["apply_patch"].GetProperty("inputSchema").GetProperty("properties").GetProperty("operations").GetProperty("type").GetString());
        Assert.Equal("array", schemas["align_nodes"].GetProperty("inputSchema").GetProperty("properties").GetProperty("nodeIds").GetProperty("type").GetString());
        Assert.True(ReadOnlyHint(schemas["list_nodes"]));
        Assert.True(ReadOnlyHint(schemas["get_document_json"]));
        Assert.True(ReadOnlyHint(schemas["render_preview"]));
        Assert.True(ReadOnlyHint(schemas["get_capabilities"]));
        Assert.Equal(schemas.Keys.Order(StringComparer.Ordinal), capabilities.GetProperty("tools").EnumerateArray().Select(tool => tool.GetString()!).Order(StringComparer.Ordinal));

        const string locator = "icons/readable.agd.json";
        var created = NativeData(await CallToolAsync("create_document", new { name = "readable", width = 1200, height = 600, outputPath = locator }));
        Assert.Equal(locator, created.GetProperty("locator").GetString());
        Assert.Equal(1, created.GetProperty("revn").GetInt64());
        Assert.False(File.Exists(Path.Combine(_testDir, created.GetProperty("id").GetString() + ".agd.json")));
        var original = await File.ReadAllTextAsync(Path.Combine(_testDir, locator));
        var exists = await CallToolAsync("create_document", new { name = "replacement", outputPath = locator });
        Assert.True(exists.GetProperty("isError").GetBoolean());
        Assert.Equal(original, await File.ReadAllTextAsync(Path.Combine(_testDir, locator)));
        AssertWireError(await CallToolAsync("create_document", new { name = "escape", outputPath = "../escape.agd.json" }), "PATH_ESCAPE");

        var group = Guid.NewGuid().ToString();
        var red = Guid.NewGuid().ToString();
        var blue = Guid.NewGuid().ToString();
        var patch = await CallToolAsync("apply_patch", new
        {
            idOrPath = locator,
            expectedRevn = 1,
            operations = new object[]
            {
                new { op = "addNode", node = new { type = "group", id = group, name = "selection", children = new object[]
                {
                    new { type = "rectangle", id = red, order = "a0", width = 100, height = 600, transform = new { x = 1100, y = 0 }, style = new { fill = new { color = "#ff0000" } } },
                    new { type = "rectangle", id = blue, order = "a1", width = 100, height = 100, style = new { fill = new { color = "#0000ff" } } }
                } } },
                new { op = "addNode", node = new { type = "rectangle", name = "unrelated-sibling", width = 1, height = 1 } }
            }
        });
        Assert.False(patch.GetProperty("isError").GetBoolean());
        var page = NativeData(await CallToolAsync("list_nodes", new { idOrPath = locator, nodeId = group, limit = 1 }));
        Assert.Equal(3, page.GetProperty("total").GetInt32());
        Assert.True(page.GetProperty("hasMore").GetBoolean());
        Assert.Equal(group, Assert.Single(page.GetProperty("nodes").EnumerateArray()).GetProperty("id").GetString());
        Assert.Equal(0, Assert.Single(page.GetProperty("nodes").EnumerateArray()).GetProperty("depth").GetInt32());
        var next = NativeData(await CallToolAsync("list_nodes", new { idOrPath = locator, nodeId = group, offset = 1, limit = 1 }));
        Assert.Equal(red, Assert.Single(next.GetProperty("nodes").EnumerateArray()).GetProperty("id").GetString());
        Assert.Equal(1, Assert.Single(next.GetProperty("nodes").EnumerateArray()).GetProperty("depth").GetInt32());
        var shallow = NativeData(await CallToolAsync("list_nodes", new { idOrPath = locator, nodeId = group, maxDepth = 0 }));
        Assert.Equal(1, shallow.GetProperty("total").GetInt32());
        var subtree = JsonDocument.Parse(NativeData(await CallToolAsync("get_document_json", new { idOrPath = locator, nodeId = group })).GetProperty("json").GetString()!);
        Assert.Equal("group", subtree.RootElement.GetProperty("type").GetString());
        Assert.Equal(2, subtree.RootElement.GetProperty("children").GetArrayLength());
        Assert.False(subtree.RootElement.TryGetProperty("canvas", out _));
        Assert.DoesNotContain("unrelated-sibling", subtree.RootElement.GetRawText());

        var saved = await File.ReadAllTextAsync(Path.Combine(_testDir, locator));
        var preview = await CallToolAsync("render_preview", new { idOrPath = locator });
        var image = Assert.Single(preview.GetProperty("content").EnumerateArray(), block => block.GetProperty("type").GetString() == "image");
        using var bitmap = SKBitmap.Decode(Convert.FromBase64String(image.GetProperty("data").GetString()!));
        Assert.Equal(512, bitmap.Width);
        Assert.Equal(256, bitmap.Height);
        Assert.Equal(SKColors.Red, bitmap.GetPixel(500, 100)); // far-right geometry survives fitting
        var sheet = await CallToolAsync("render_contact_sheet", new { paths = "[\"" + locator + "\"]", cell = 128 });
        var sheetImage = Assert.Single(sheet.GetProperty("content").EnumerateArray(), block => block.GetProperty("type").GetString() == "image");
        using var sheetBitmap = SKBitmap.Decode(Convert.FromBase64String(sheetImage.GetProperty("data").GetString()!));
        Assert.NotNull(sheetBitmap);
        Assert.Equal(saved, await File.ReadAllTextAsync(Path.Combine(_testDir, locator)));
        AssertWireError(await CallToolAsync("list_nodes", new { idOrPath = locator, limit = 1001 }), "INVALID_ARGUMENTS");
        AssertWireError(await CallToolAsync("render_preview", new { idOrPath = locator, width = 2048, height = 100, scale = 4 }), "INVALID_DIMENSIONS");

        var failed = await CallToolAsync("apply_patch", new
        {
            idOrPath = locator,
            expectedRevn = 2,
            operations = new object[] { new { op = "setCanvas", width = 100 }, new { op = "deleteNode", nodeId = Guid.NewGuid() } }
        });
        AssertWireError(failed, "NODE_NOT_FOUND");
        Assert.Equal(1, NativeEnvelope(failed).GetProperty("error").GetProperty("operationIndex").GetInt32());
        Assert.Equal(saved, await File.ReadAllTextAsync(Path.Combine(_testDir, locator)));
        AssertWireError(await CallToolAsync("apply_patch", new { idOrPath = locator, expectedRevn = 1, operations = new object[] { new { op = "setCanvas", width = 100 } } }), "REVISION_CONFLICT");
        var duplicate = await SendRawToolCallAsync("apply_patch", "{\"idOrPath\":\"" + locator + "\",\"operations\":[{\"op\":\"setCanvas\",\"op\":\"setCanvas\",\"width\":100}]}");
        AssertWireError(duplicate, "INVALID_ARGUMENTS");
        Assert.Equal(saved, await File.ReadAllTextAsync(Path.Combine(_testDir, locator)));
    }

    [Theory]
    [InlineData(-1d, 100d)]
    [InlineData(0d, 100d)]
    [InlineData(1d, 1e308)]
    public async Task Readiness_InvalidCanvas_IsStructuredFailureWithoutSourceChanges(double width, double height)
    {
        await StartServerAsync();
        await InitializeAsync();
        var document = new DocumentEngine().CreateDocument("invalid-canvas");
        var json = JsonNode.Parse(DocumentSerializer.Serialize(document))!;
        json["canvas"]!["width"] = width;
        json["canvas"]!["height"] = height;
        var path = Path.Combine(_testDir, "invalid.agd.json");
        var before = json.ToJsonString();
        await File.WriteAllTextAsync(path, before);
        AssertWireError(await CallToolAsync("render_preview", new { idOrPath = "invalid.agd.json" }), "INVALID_DIMENSIONS");
        Assert.Equal(before, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Readiness_ForceOverwriteOddAndExplicitPreviews_KeepDocumentSemantics()
    {
        await StartServerAsync();
        await InitializeAsync();
        const string locator = "odd.agd.json";
        await CallToolAsync("create_document", new { name = "original", width = 100, height = 100, outputPath = locator });
        var created = NativeData(await CallToolAsync("create_document", new { name = "replacement", width = 997, height = 333, outputPath = locator, force = true }));
        Assert.Equal("replacement", created.GetProperty("name").GetString());
        var saved = await File.ReadAllTextAsync(Path.Combine(_testDir, locator));
        var automatic = await CallToolAsync("render_preview", new { idOrPath = locator });
        var autoImage = Assert.Single(automatic.GetProperty("content").EnumerateArray(), block => block.GetProperty("type").GetString() == "image");
        using var autoBitmap = SKBitmap.Decode(Convert.FromBase64String(autoImage.GetProperty("data").GetString()!));
        Assert.Equal(512, autoBitmap.Width);
        Assert.Equal((int)Math.Round(333d * 512 / 997, MidpointRounding.AwayFromZero), autoBitmap.Height);
        var explicitPreview = await CallToolAsync("render_preview", new { idOrPath = locator, width = 200, height = 100, scale = 2 });
        var explicitImage = Assert.Single(explicitPreview.GetProperty("content").EnumerateArray(), block => block.GetProperty("type").GetString() == "image");
        using var explicitBitmap = SKBitmap.Decode(Convert.FromBase64String(explicitImage.GetProperty("data").GetString()!));
        Assert.Equal(400, explicitBitmap.Width);
        Assert.Equal(200, explicitBitmap.Height);
        Assert.Equal(saved, await File.ReadAllTextAsync(Path.Combine(_testDir, locator)));
    }

    [Fact]
    public async Task Readiness_TypedArrayCountBounds_RejectWithoutWrites()
    {
        await StartServerAsync();
        await InitializeAsync();
        var created = NativeData(await CallToolAsync("create_document", new { name = "bounds" }));
        var id = created.GetProperty("id").GetString()!;
        var operation = new { op = "setCanvas", width = 100 };
        Assert.False((await CallToolAsync("apply_patch", new { idOrPath = id, operations = Enumerable.Repeat(operation, 500).ToArray() })).GetProperty("isError").GetBoolean());
        var path = Path.Combine(_testDir, id + ".agd.json");
        var before = await File.ReadAllTextAsync(path);
        AssertWireError(await CallToolAsync("apply_patch", new { idOrPath = id, operations = Enumerable.Repeat(operation, 501).ToArray() }), "RESOURCE_LIMIT");
        AssertWireError(await CallToolAsync("align_nodes", new { idOrPath = id, nodeIds = Enumerable.Range(0, 1001).Select(_ => Guid.NewGuid().ToString()).ToArray(), mode = "left" }), "INVALID_ARGUMENTS");
        AssertWireError(await CallToolAsync("align_nodes", new { idOrPath = id, nodeIds = Enumerable.Range(0, 1000).Select(_ => Guid.NewGuid().ToString()).ToArray(), mode = "left" }), "NODE_NOT_FOUND");
        AssertWireError(await CallToolAsync("align_nodes", new { idOrPath = id, nodeIds = new[] { new string('a', 65) }, mode = "left" }), "INVALID_ARGUMENTS");
        Assert.Equal(before, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Readiness_TwoServers_ExclusiveCreateHasExactlyOneWinner()
    {
        await StartServerAsync();
        await InitializeAsync();
        using var second = new ProtocolTests(_output);
        await second.StartServerAsync(_testDir);
        await second.InitializeAsync();
        var results = await Task.WhenAll(
            CallToolAsync("create_document", new { name = "first", outputPath = "race.agd.json" }),
            second.CallToolAsync("create_document", new { name = "second", outputPath = "race.agd.json" }));
        Assert.Single(results, result => !result.GetProperty("isError").GetBoolean());
        Assert.Single(results, result => result.GetProperty("isError").GetBoolean());
        var document = DocumentSerializer.Deserialize(await File.ReadAllTextAsync(Path.Combine(_testDir, "race.agd.json")))!;
        Assert.Contains(document.Name, new[] { "first", "second" });
        Assert.Single(Directory.GetFiles(_testDir, "*.agd.json"));
    }

    [Fact]
    public async Task Readiness_StdioMessageByteBoundary_RejectsOversizeAndRecovers()
    {
        await StartServerAsync();
        await InitializeAsync();
        var request = "{\"jsonrpc\":\"2.0\",\"id\":501,\"method\":\"tools/list\",\"params\":{}}";
        Assert.Equal(8 * 1024 * 1024, FTPlus.AgentGraphicDesign.Core.Wire.AgdWireParser.MaxMessageBytes);
        var exact = new string(' ', 8 * 1024 * 1024 - Encoding.UTF8.GetByteCount(request)) + request;
        await _stdin!.WriteLineAsync(exact);
        await _stdin.FlushAsync();
        using var accepted = JsonDocument.Parse((await _stdout!.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30)))!);
        Assert.True(accepted.RootElement.TryGetProperty("result", out _));
        await _stdin.WriteAsync(exact + "\r");
        await _stdin.FlushAsync();
        await Task.Delay(20); // CR and LF deliberately cross separate writes/reads.
        await _stdin.WriteAsync("\n");
        await _stdin.FlushAsync();
        using var crlf = JsonDocument.Parse((await _stdout.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30)))!);
        Assert.True(crlf.RootElement.TryGetProperty("result", out _));
        await _stdin.WriteAsync(" " + exact + "\r");
        await _stdin.FlushAsync();
        await Task.Delay(20);
        await _stdin.WriteAsync("\n");
        await _stdin.FlushAsync();
        using var crlfOver = JsonDocument.Parse((await _stdout.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30)))!);
        Assert.Equal(-32600, crlfOver.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(JsonValueKind.Null, crlfOver.RootElement.GetProperty("id").ValueKind);
        Assert.True((await SendRequestAsync("tools/list", new { })).RootElement.TryGetProperty("result", out _));
        await _stdin.WriteLineAsync(" " + exact);
        await _stdin.FlushAsync();
        using var rejected = JsonDocument.Parse((await _stdout.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30)))!);
        Assert.True(rejected.RootElement.TryGetProperty("error", out _));
        Assert.Equal(-32600, rejected.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(JsonValueKind.Null, rejected.RootElement.GetProperty("id").ValueKind);
        Assert.Equal("Message exceeds the maximum accepted size.", rejected.RootElement.GetProperty("error").GetProperty("message").GetString());
        Assert.True((await SendRequestAsync("tools/list", new { })).RootElement.TryGetProperty("result", out _));
    }

    [Fact]
    public async Task Readiness_NativeGuideRecipe_ExecutesGuardedLayersAndMask()
    {
        await StartServerAsync();
        await InitializeAsync();
        var asset = await CreatePixelAssetAsync("guide.pixels", 64, 64);
        var guide = await File.ReadAllTextAsync(Path.Combine(RepositoryPaths.Root, "docs", "agent-guide.md"));
        var section = guide[(guide.IndexOf("## Native layered still-pixel recipe", StringComparison.Ordinal))..];
        var requestText = Regex.Match(section, "```json\\s*([\\s\\S]*?)\\s*```").Groups[1].Value
            .Replace("<ASSET_ID>", asset.AssetId).Replace("<SOURCE_DIGEST>", asset.SourceDigest).Replace("<LAYER_ID>", asset.LayerId);
        using var request = JsonDocument.Parse(requestText);
        var edited = await CallToolAsync("edit_pixel_asset", new { assetDirectory = "guide.pixels", request = request.RootElement });
        Assert.False(edited.GetProperty("isError").GetBoolean());
        Assert.Equal(2, NativeData(edited).GetProperty("state").GetProperty("layers").GetArrayLength());
        var highlight = NativeData(edited).GetProperty("state").GetProperty("layers")[1].GetProperty("id").GetString();
        var mask = NativeData(await CallToolAsync("inspect_pixel_region", new { assetDirectory = "guide.pixels", layerId = highlight, target = "mask", rect = new { x = 20, y = 20, width = 1, height = 1 } }));
        Assert.Equal(0, Assert.Single(mask.GetProperty("samples").EnumerateArray()).GetInt32());
        var region = NativeData(await CallToolAsync("inspect_pixel_region", new { assetDirectory = "guide.pixels", layerId = asset.LayerId, target = "pixels", rect = new { x = 16, y = 16, width = 1, height = 1 } }));
        Assert.Equal("#336699ff", Assert.Single(region.GetProperty("samples").EnumerateArray()).GetString());
        var valid = NativeData(await CallToolAsync("validate_pixel_asset", new { assetDirectory = "guide.pixels" }));
        Assert.True(valid.GetProperty("valid").GetBoolean());
        var preview = await CallToolAsync("preview_pixel_asset", new { assetDirectory = "guide.pixels" });
        var image = Assert.Single(preview.GetProperty("content").EnumerateArray(), block => block.GetProperty("type").GetString() == "image");
        using var bitmap = SKBitmap.Decode(Convert.FromBase64String(image.GetProperty("data").GetString()!));
        Assert.Equal(64, bitmap.Width);
        Assert.Equal(64, bitmap.Height);
        Assert.Equal(SKColor.Parse("#336699"), bitmap.GetPixel(18, 18));
        Assert.Equal(0, bitmap.GetPixel(0, 0).Alpha);
        AssertWireError(await CallToolAsync("edit_pixel_asset", new { assetDirectory = "guide.pixels", request = request.RootElement }), "REVISION_CONFLICT");
    }

    [Fact]
    public async Task Readiness_UnsupportedArrayShapes_ReturnStructuredErrorsWithoutWrites()
    {
        await StartServerAsync();
        await InitializeAsync();
        var created = NativeData(await CallToolAsync("create_document", new { name = "shapes" }));
        var id = created.GetProperty("id").GetString()!;
        var path = Path.Combine(_testDir, id + ".agd.json");
        var before = await File.ReadAllTextAsync(path);
        AssertWireError(await CallToolAsync("apply_patch", new { idOrPath = id, operations = "[]" }), "INVALID_ARGUMENTS");
        AssertWireError(await CallToolAsync("align_nodes", new { idOrPath = id, nodeIds = created.GetProperty("rootId").GetString(), mode = "left" }), "INVALID_ARGUMENTS");
        Assert.Equal(before, await File.ReadAllTextAsync(path));
    }

    [Theory]
    [InlineData(1200, 1, 512, 1)]
    [InlineData(1, 1200, 1, 512)]
    public async Task Readiness_ThinCanvas_DefaultPreviewProducesAtLeastOnePixel(int width, int height, int expectedWidth, int expectedHeight)
    {
        await StartServerAsync();
        await InitializeAsync();
        var created = NativeData(await CallToolAsync("create_document", new { name = "thin", width, height }));
        var id = created.GetProperty("id").GetString()!;
        var preview = await CallToolAsync("render_preview", new { idOrPath = id });
        var image = Assert.Single(preview.GetProperty("content").EnumerateArray(), block => block.GetProperty("type").GetString() == "image");
        using var bitmap = SKBitmap.Decode(Convert.FromBase64String(image.GetProperty("data").GetString()!));
        Assert.Equal(expectedWidth, bitmap.Width);
        Assert.Equal(expectedHeight, bitmap.Height);
    }

    [Fact]
    public async Task WorkingDirectory_CommandLineArg_IsRespected()
    {
        var subDir = Path.Combine(_testDir, "subworkdir");
        Directory.CreateDirectory(subDir);

        await StartServerAsync(subDir);
        await InitializeAsync();

        var createResponse = await CallToolAsync("create_document", new { name = "subdir-test", width = 100, height = 100 });
        var createResult = ParseToolResult(createResponse);
        Assert.True(createResult.TryGetProperty("id", out _));

        var files = Directory.GetFiles(subDir, "*.agd.json");
        Assert.Single(files);
        Assert.Empty(Directory.GetFiles(_testDir, "*.agd.json"));
    }

    // ---- Native pixel tools (P6, archived plan slice 7): the eight *_pixel_* tools over the real server ----

    [Fact]
    public async Task ToolsList_IncludesNativePixelToolsWithReadOnlyAnnotations()
    {
        await StartServerAsync();
        await InitializeAsync();

        var response = await SendRequestAsync("tools/list", new { });
        var byName = response.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray()
            .ToDictionary(tool => tool.GetProperty("name").GetString()!);

        foreach (var name in new[]
                 {
                     "create_pixel_asset", "inspect_pixel_asset", "inspect_pixel_region", "edit_pixel_asset",
                     "validate_pixel_asset", "preview_pixel_asset", "export_pixel_asset", "check_pixel_export"
                 })
        {
            Assert.Contains(name, byName.Keys);
        }

        // Read-only annotations are true for inspect/region/validate/preview/check.
        Assert.True(ReadOnlyHint(byName["inspect_pixel_asset"]));
        Assert.True(ReadOnlyHint(byName["inspect_pixel_region"]));
        Assert.True(ReadOnlyHint(byName["validate_pixel_asset"]));
        Assert.True(ReadOnlyHint(byName["preview_pixel_asset"]));
        Assert.True(ReadOnlyHint(byName["check_pixel_export"]));
        // ... and not true for the source-writing tools.
        Assert.NotEqual(true, ReadOnlyHint(byName["create_pixel_asset"]));
        Assert.NotEqual(true, ReadOnlyHint(byName["edit_pixel_asset"]));
        Assert.NotEqual(true, ReadOnlyHint(byName["export_pixel_asset"]));
    }

    [Fact]
    public async Task CreatePixelAsset_ThenInspect_RoundTripsSnapshotAndDimensions()
    {
        await StartServerAsync();
        await InitializeAsync();
        var asset = await CreatePixelAssetAsync("icon.pixels");

        var result = await CallToolAsync("inspect_pixel_asset", new { assetDirectory = "icon.pixels" });

        Assert.False(result.GetProperty("isError").GetBoolean());
        var data = NativeData(result);
        Assert.Equal(asset.AssetId, data.GetProperty("snapshot").GetProperty("assetId").GetString());
        Assert.Equal(4, data.GetProperty("width").GetInt32());
        Assert.Equal(4, data.GetProperty("height").GetInt32());
        Assert.Single(data.GetProperty("layers").EnumerateArray());
    }

    [Fact]
    public async Task EditPixelAsset_AppliesGuardedFill_AndAdvancesRevision()
    {
        await StartServerAsync();
        await InitializeAsync();
        var asset = await CreatePixelAssetAsync("icon.pixels");

        var result = await CallToolAsync("edit_pixel_asset", new
        {
            assetDirectory = "icon.pixels",
            request = FillRequest(asset)
        });

        Assert.False(result.GetProperty("isError").GetBoolean());
        var data = NativeData(result);
        Assert.True(data.GetProperty("changed").GetBoolean());
        Assert.Equal(asset.Revn + 1, data.GetProperty("state").GetProperty("snapshot").GetProperty("revn").GetInt64());
    }

    [Fact]
    public async Task EditPixelAsset_StaleSnapshot_IsRevisionConflict()
    {
        await StartServerAsync();
        await InitializeAsync();
        var asset = await CreatePixelAssetAsync("icon.pixels");
        // First edit advances the revision, so the same snapshot is now stale.
        Assert.False((await CallToolAsync("edit_pixel_asset", new { assetDirectory = "icon.pixels", request = FillRequest(asset) }))
            .GetProperty("isError").GetBoolean());

        var result = await CallToolAsync("edit_pixel_asset", new { assetDirectory = "icon.pixels", request = FillRequest(asset) });

        Assert.True(result.GetProperty("isError").GetBoolean());
        Assert.Equal("REVISION_CONFLICT", NativeEnvelope(result).GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task ValidatePixelAsset_ValidAndCorrupt_UseCompletedReportConvention()
    {
        await StartServerAsync();
        await InitializeAsync();
        await CreatePixelAssetAsync("icon.pixels");

        var valid = await CallToolAsync("validate_pixel_asset", new { assetDirectory = "icon.pixels" });
        Assert.False(valid.GetProperty("isError").GetBoolean()); // a completed report is not an execution error
        Assert.True(NativeData(valid).GetProperty("valid").GetBoolean());

        // Corrupt the intrinsic source: still a completed report, ok:true / valid:false / IsError=false.
        await File.WriteAllTextAsync(Path.Combine(_testDir, "icon.pixels", "asset.agd-pixels.json"), "{ not valid json }");
        var invalid = await CallToolAsync("validate_pixel_asset", new { assetDirectory = "icon.pixels" });
        Assert.False(invalid.GetProperty("isError").GetBoolean());
        var envelope = NativeEnvelope(invalid);
        Assert.True(envelope.GetProperty("ok").GetBoolean());
        Assert.False(envelope.GetProperty("data").GetProperty("valid").GetBoolean());
    }

    [Fact]
    public async Task ExportThenCheck_MatchesThenDriftsAfterEdit()
    {
        await StartServerAsync();
        await InitializeAsync();
        var asset = await CreatePixelAssetAsync("icon.pixels");

        Assert.False((await CallToolAsync("export_pixel_asset", new { assetDirectory = "icon.pixels", outputPath = "out.png" }))
            .GetProperty("isError").GetBoolean());
        Assert.True(File.Exists(Path.Combine(_testDir, "out.png")));

        var match = await CallToolAsync("check_pixel_export", new { assetDirectory = "icon.pixels", outputPath = "out.png" });
        Assert.False(match.GetProperty("isError").GetBoolean());
        Assert.True(NativeData(match).GetProperty("matches").GetBoolean());

        // Change the source so the exported PNG no longer reflects a fresh evaluation.
        await CallToolAsync("edit_pixel_asset", new { assetDirectory = "icon.pixels", request = FillRequest(asset) });

        var drift = await CallToolAsync("check_pixel_export", new { assetDirectory = "icon.pixels", outputPath = "out.png" });
        // A completed mismatch: IsError=true, ok:false, comparison still in data, typed drift code.
        Assert.True(drift.GetProperty("isError").GetBoolean());
        var envelope = NativeEnvelope(drift);
        Assert.False(envelope.GetProperty("ok").GetBoolean());
        Assert.Equal("PIXEL_OUTPUT_DRIFT", envelope.GetProperty("error").GetProperty("code").GetString());
        Assert.False(envelope.GetProperty("data").GetProperty("matches").GetBoolean());
    }

    [Fact]
    public async Task ExportPixelAsset_ExistingDestinationWithoutOverwrite_IsDestinationExists()
    {
        await StartServerAsync();
        await InitializeAsync();
        await CreatePixelAssetAsync("icon.pixels");
        Assert.False((await CallToolAsync("export_pixel_asset", new { assetDirectory = "icon.pixels", outputPath = "out.png" }))
            .GetProperty("isError").GetBoolean());

        var second = await CallToolAsync("export_pixel_asset", new { assetDirectory = "icon.pixels", outputPath = "out.png" });
        Assert.True(second.GetProperty("isError").GetBoolean());
        Assert.Equal("PIXEL_DESTINATION_EXISTS", NativeEnvelope(second).GetProperty("error").GetProperty("code").GetString());

        var overwrite = await CallToolAsync("export_pixel_asset",
            new { assetDirectory = "icon.pixels", outputPath = "out.png", overwrite = true });
        Assert.False(overwrite.GetProperty("isError").GetBoolean());
    }

    [Fact]
    public async Task PreviewPixelAsset_ReturnsInlineImageWithoutRepeatingBytesInData()
    {
        await StartServerAsync();
        await InitializeAsync();
        await CreatePixelAssetAsync("icon.pixels");

        var result = await CallToolAsync("preview_pixel_asset", new { assetDirectory = "icon.pixels" });

        Assert.False(result.GetProperty("isError").GetBoolean());
        // One inline image/png content block rides beside the envelope text block.
        var blocks = result.GetProperty("content").EnumerateArray().ToList();
        Assert.Contains(blocks, block => block.TryGetProperty("type", out var type) && type.GetString() == "image"
            && block.GetProperty("mimeType").GetString() == "image/png");
        var envelope = NativeEnvelope(result);
        Assert.True(envelope.GetProperty("ok").GetBoolean());
        Assert.Equal("inline", envelope.GetProperty("data").GetProperty("delivery").GetString());
        // Image bytes are not repeated inside the data envelope's text.
        Assert.False(envelope.GetProperty("data").TryGetProperty("pngBytes", out _));
    }

    [Fact]
    public async Task InspectPixelRegion_ReadsActualSamples()
    {
        await StartServerAsync();
        await InitializeAsync();
        var asset = await CreatePixelAssetAsync("icon.pixels");

        var result = await CallToolAsync("inspect_pixel_region", new
        {
            assetDirectory = "icon.pixels",
            layerId = asset.LayerId,
            target = "pixels",
            rect = new { x = 0, y = 0, width = 2, height = 2 }
        });

        Assert.False(result.GetProperty("isError").GetBoolean());
        Assert.Equal(4, NativeData(result).GetProperty("samples").GetArrayLength());
    }

    [Fact]
    public async Task PixelTool_AssetDirectoryOutsideRoot_IsPathEscape()
    {
        await StartServerAsync();
        await InitializeAsync();

        var result = await CallToolAsync("inspect_pixel_asset", new { assetDirectory = "../../../escape.pixels" });

        Assert.True(result.GetProperty("isError").GetBoolean());
        Assert.Equal("PATH_ESCAPE", NativeEnvelope(result).GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task InspectPixelAsset_Missing_IsDocumentNotFound()
    {
        await StartServerAsync();
        await InitializeAsync();

        var result = await CallToolAsync("inspect_pixel_asset", new { assetDirectory = "absent.pixels" });

        Assert.True(result.GetProperty("isError").GetBoolean());
        Assert.Equal("DOCUMENT_NOT_FOUND", NativeEnvelope(result).GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task EditPixelAsset_ReplaceInputThroughSymlinkedParent_IsPathEscape()
    {
        await StartServerAsync();
        await InitializeAsync();
        var asset = await CreatePixelAssetAsync("icon.pixels");

        // A symlinked directory inside the working root that points outside it. A lexical prefix
        // check accepts "linkdir/payload.bin"; the segment-walking reparse check must reject it so
        // a replacement input cannot alias a file outside the confined root.
        Directory.CreateSymbolicLink(Path.Combine(_testDir, "linkdir"), Path.GetTempPath());

        var result = await CallToolAsync("edit_pixel_asset", new
        {
            assetDirectory = "icon.pixels",
            request = new
            {
                expectedSnapshot = new { assetId = asset.AssetId, revn = asset.Revn, sourceDigest = asset.SourceDigest },
                operations = new object[]
                {
                    new
                    {
                        op = "replace", layer = asset.LayerId, target = "pixels",
                        rect = new { x = 0, y = 0, width = 4, height = 4 }, inputPath = "linkdir/payload.bin"
                    }
                }
            }
        });

        Assert.True(result.GetProperty("isError").GetBoolean());
        Assert.Equal("PATH_ESCAPE", NativeEnvelope(result).GetProperty("error").GetProperty("code").GetString());
    }

    // ----- P6, archived plan slice 8: real-MCP wire-fidelity, end-to-end through the spawned agd-mcp process -----
    // These drive the server with byte-exact JSON-RPC frames that a typed object cannot express
    // (duplicate members, precise numeric spelling, exact byte lengths), proving the S2 original-byte
    // ingress gate runs before tool dispatch in the real process — not just in the Core unit parser.
    // The gate short-circuits with an error envelope (isError=true) carrying the contract error code.

    [Fact]
    public async Task WireFidelity_DuplicateArgumentMember_IsInvalidArguments()
    {
        await StartServerAsync();
        await InitializeAsync();
        var asset = await CreatePixelAssetAsync("icon.pixels");

        // A duplicate "assetDirectory" argument. A typed dictionary would silently keep the last.
        var result = await SendRawToolCallAsync("edit_pixel_asset", Fill(
            """
            {"assetDirectory":"icon.pixels","assetDirectory":"icon.pixels","request":{"expectedSnapshot":{"assetId":"{ASSET}","revn":{REVN},"sourceDigest":"{DIGEST}"},"operations":[{"op":"fill","layer":"{LAYER}","target":"pixels","rect":{"x":0,"y":0,"width":4,"height":4},"rgba":"#ff0000ff"}]}}
            """, asset));

        AssertWireError(result, "INVALID_ARGUMENTS");
    }

    [Fact]
    public async Task WireFidelity_DuplicateRequestMember_IsInvalidArguments()
    {
        await StartServerAsync();
        await InitializeAsync();
        var asset = await CreatePixelAssetAsync("icon.pixels");

        // Two "operations" members inside the request object.
        var result = await SendRawToolCallAsync("edit_pixel_asset", Fill(
            """
            {"assetDirectory":"icon.pixels","request":{"expectedSnapshot":{"assetId":"{ASSET}","revn":{REVN},"sourceDigest":"{DIGEST}"},"operations":[{"op":"fill","layer":"{LAYER}","target":"pixels","rect":{"x":0,"y":0,"width":4,"height":4},"rgba":"#ff0000ff"}],"operations":[]}}
            """, asset));

        AssertWireError(result, "INVALID_ARGUMENTS");
    }

    [Fact]
    public async Task WireFidelity_EscapedDuplicateNestedMember_IsInvalidArguments()
    {
        await StartServerAsync();
        await InitializeAsync();
        var asset = await CreatePixelAssetAsync("icon.pixels");

        // "op" decodes to "op"; the gate keys on the DECODED name so the escaped spelling
        // collides with the plain "op" and both are rejected, unlike a last-value binder.
        var result = await SendRawToolCallAsync("edit_pixel_asset", Fill(
            """
            {"assetDirectory":"icon.pixels","request":{"expectedSnapshot":{"assetId":"{ASSET}","revn":{REVN},"sourceDigest":"{DIGEST}"},"operations":[{"op":"fill","op":"fill","layer":"{LAYER}","target":"pixels","rect":{"x":0,"y":0,"width":4,"height":4},"rgba":"#ff0000ff"}]}}
            """, asset));

        AssertWireError(result, "INVALID_ARGUMENTS");
    }

    [Fact]
    public async Task WireFidelity_UnknownRequestMember_IsInvalidArguments()
    {
        await StartServerAsync();
        await InitializeAsync();
        var asset = await CreatePixelAssetAsync("icon.pixels");

        var result = await SendRawToolCallAsync("edit_pixel_asset", Fill(
            """
            {"assetDirectory":"icon.pixels","request":{"expectedSnapshot":{"assetId":"{ASSET}","revn":{REVN},"sourceDigest":"{DIGEST}"},"operations":[{"op":"fill","layer":"{LAYER}","target":"pixels","rect":{"x":0,"y":0,"width":4,"height":4},"rgba":"#ff0000ff"}],"unexpected":1}}
            """, asset));

        AssertWireError(result, "INVALID_ARGUMENTS");
    }

    [Fact]
    public async Task WireFidelity_ExponentNumericToken_IsInvalidFormat()
    {
        await StartServerAsync();
        await InitializeAsync();
        var asset = await CreatePixelAssetAsync("icon.pixels");

        // revn spelled as "1e0" — a legal JSON number, but the contract forbids exponent tokens.
        var result = await SendRawToolCallAsync("edit_pixel_asset", Fill(
            """
            {"assetDirectory":"icon.pixels","request":{"expectedSnapshot":{"assetId":"{ASSET}","revn":1e0,"sourceDigest":"{DIGEST}"},"operations":[{"op":"fill","layer":"{LAYER}","target":"pixels","rect":{"x":0,"y":0,"width":4,"height":4},"rgba":"#ff0000ff"}]}}
            """, asset));

        AssertWireError(result, "INVALID_FORMAT");
    }

    [Fact]
    public async Task WireFidelity_ExcessOpacityFractionDigits_IsInvalidFormat()
    {
        await StartServerAsync();
        await InitializeAsync();
        var asset = await CreatePixelAssetAsync("icon.pixels");

        // Seven fractional digits exceeds opacity's six-digit millionths grammar.
        var result = await SendRawToolCallAsync("edit_pixel_asset", Fill(
            """
            {"assetDirectory":"icon.pixels","request":{"expectedSnapshot":{"assetId":"{ASSET}","revn":{REVN},"sourceDigest":"{DIGEST}"},"operations":[{"op":"setOpacity","layer":"{LAYER}","opacity":0.1234567}]}}
            """, asset));

        AssertWireError(result, "INVALID_FORMAT");
    }

    [Fact]
    public async Task WireFidelity_ExactMillionthOpacity_IsAdmittedAndApplied()
    {
        await StartServerAsync();
        await InitializeAsync();
        var asset = await CreatePixelAssetAsync("icon.pixels");

        // Exactly six fractional digits (123456 millionths) is admitted and applied verbatim.
        var result = await SendRawToolCallAsync("edit_pixel_asset", Fill(
            """
            {"assetDirectory":"icon.pixels","request":{"expectedSnapshot":{"assetId":"{ASSET}","revn":{REVN},"sourceDigest":"{DIGEST}"},"operations":[{"op":"setOpacity","layer":"{LAYER}","opacity":0.123456}]}}
            """, asset));

        Assert.False(result.GetProperty("isError").GetBoolean());
        Assert.True(NativeData(result).GetProperty("changed").GetBoolean());
    }

    [Fact]
    public async Task WireFidelity_RevisionAboveDoublePrecision_SurvivesAsRevisionConflict()
    {
        await StartServerAsync();
        await InitializeAsync();
        var asset = await CreatePixelAssetAsync("icon.pixels");

        // 9007199254740993 = 2^53 + 1 cannot be represented as a double; if the gate parsed the
        // token through a float it would collapse to 2^53. The correct assetId/sourceDigest but a
        // huge revn must be ADMITTED (not INVALID_FORMAT) and reach the service as a plain conflict,
        // proving the original integer token survived intact.
        var result = await SendRawToolCallAsync("edit_pixel_asset", Fill(
            """
            {"assetDirectory":"icon.pixels","request":{"expectedSnapshot":{"assetId":"{ASSET}","revn":9007199254740993,"sourceDigest":"{DIGEST}"},"operations":[{"op":"fill","layer":"{LAYER}","target":"pixels","rect":{"x":0,"y":0,"width":4,"height":4},"rgba":"#ff0000ff"}]}}
            """, asset));

        AssertWireError(result, "REVISION_CONFLICT");
    }

    [Fact]
    public async Task WireFidelity_OversizedRequest_IsResourceLimit()
    {
        await StartServerAsync();
        await InitializeAsync();
        var asset = await CreatePixelAssetAsync("icon.pixels");

        // Legal JSON whitespace pads the request object just past the 1 MiB original-byte bound.
        var padding = new string(' ', (1 << 20) + 16);
        var request = "{\"expectedSnapshot\":{\"assetId\":\"" + asset.AssetId + "\",\"revn\":" + asset.Revn +
            ",\"sourceDigest\":\"" + asset.SourceDigest + "\"}," + padding +
            "\"operations\":[{\"op\":\"fill\",\"layer\":\"" + asset.LayerId +
            "\",\"target\":\"pixels\",\"rect\":{\"x\":0,\"y\":0,\"width\":4,\"height\":4},\"rgba\":\"#ff0000ff\"}]}";
        var result = await SendRawToolCallAsync("edit_pixel_asset",
            "{\"assetDirectory\":\"icon.pixels\",\"request\":" + request + "}");

        AssertWireError(result, "RESOURCE_LIMIT");
    }

    // ----- P6, archived plan slice 8: acceptance-matrix lifecycle at boundary dimensions, through the server -----

    [Theory]
    [InlineData(37, 23)]
    [InlineData(64, 64)]
    [InlineData(1, 64)]
    public async Task PixelLifecycle_AtBoundaryDimensions_ExportChecksThenDriftsThroughTheServer(int width, int height)
    {
        await StartServerAsync();
        await InitializeAsync();
        var created = await CreatePixelAssetAsync("icon.pixels", width, height);

        var edit = await CallToolAsync("edit_pixel_asset", new
        {
            assetDirectory = "icon.pixels",
            request = FillWholeCanvas(created, width, height, "#336699c0")
        });
        Assert.False(edit.GetProperty("isError").GetBoolean());

        Assert.False((await CallToolAsync("export_pixel_asset", new { assetDirectory = "icon.pixels", outputPath = "out.png" }))
            .GetProperty("isError").GetBoolean());
        Assert.True(File.Exists(Path.Combine(_testDir, "out.png")));

        var match = await CallToolAsync("check_pixel_export", new { assetDirectory = "icon.pixels", outputPath = "out.png" });
        Assert.False(match.GetProperty("isError").GetBoolean());
        Assert.True(NativeData(match).GetProperty("matches").GetBoolean());

        // A render-changing edit makes the already-exported PNG drift under the reference evaluator.
        var current = await InspectSnapshotAsync("icon.pixels", created.LayerId);
        Assert.False((await CallToolAsync("edit_pixel_asset", new
        {
            assetDirectory = "icon.pixels",
            request = FillWholeCanvas(current, width, height, "#00ff00ff")
        })).GetProperty("isError").GetBoolean());

        var drift = await CallToolAsync("check_pixel_export", new { assetDirectory = "icon.pixels", outputPath = "out.png" });
        Assert.True(drift.GetProperty("isError").GetBoolean());
        Assert.Equal("PIXEL_OUTPUT_DRIFT", NativeEnvelope(drift).GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task ExportPixelAsset_SymlinkedDestination_IsPathEscapeThroughTheServer()
    {
        await StartServerAsync();
        await InitializeAsync();
        await CreatePixelAssetAsync("icon.pixels");

        // A symlink standing in for the output leaf is an aliased destination the writer must refuse
        // even with overwrite, so a second name can never observe the written bytes.
        File.CreateSymbolicLink(Path.Combine(_testDir, "out.png"), Path.Combine(Path.GetTempPath(), "agd-alias-target.png"));

        var result = await CallToolAsync("export_pixel_asset",
            new { assetDirectory = "icon.pixels", outputPath = "out.png", overwrite = true });

        Assert.True(result.GetProperty("isError").GetBoolean());
        Assert.Equal("PATH_ESCAPE", NativeEnvelope(result).GetProperty("error").GetProperty("code").GetString());
    }

    private sealed record CreatedPixelAsset(string AssetId, long Revn, string SourceDigest, string LayerId);

    private async Task<CreatedPixelAsset> CreatePixelAssetAsync(string directory, int width = 4, int height = 4, string name = "icon")
    {
        var result = await CallToolAsync("create_pixel_asset", new { assetDirectory = directory, name, width, height });
        Assert.False(result.GetProperty("isError").GetBoolean());
        var snapshot = NativeData(result).GetProperty("state").GetProperty("snapshot");
        return new CreatedPixelAsset(
            snapshot.GetProperty("assetId").GetString()!,
            snapshot.GetProperty("revn").GetInt64(),
            snapshot.GetProperty("sourceDigest").GetString()!,
            NativeData(result).GetProperty("initialLayerId").GetString()!);
    }

    private static object FillRequest(CreatedPixelAsset asset) => new
    {
        expectedSnapshot = new { assetId = asset.AssetId, revn = asset.Revn, sourceDigest = asset.SourceDigest },
        operations = new object[]
        {
            new { op = "fill", layer = asset.LayerId, target = "pixels", rect = new { x = 0, y = 0, width = 4, height = 4 }, rgba = "#ff0000ff" }
        }
    };

    private static object FillWholeCanvas(CreatedPixelAsset asset, int width, int height, string rgba) => new
    {
        expectedSnapshot = new { assetId = asset.AssetId, revn = asset.Revn, sourceDigest = asset.SourceDigest },
        operations = new object[]
        {
            new { op = "fill", layer = asset.LayerId, target = "pixels", rect = new { x = 0, y = 0, width, height }, rgba }
        }
    };

    /// <summary>Substitutes the {ASSET}/{REVN}/{DIGEST}/{LAYER} tokens in a raw JSON template.</summary>
    private static string Fill(string template, CreatedPixelAsset asset) => template
        .Replace("{ASSET}", asset.AssetId)
        .Replace("{REVN}", asset.Revn.ToString())
        .Replace("{DIGEST}", asset.SourceDigest)
        .Replace("{LAYER}", asset.LayerId);

    /// <summary>Re-reads the current snapshot so a follow-up guarded edit can carry the fresh token.</summary>
    private async Task<CreatedPixelAsset> InspectSnapshotAsync(string directory, string layerId)
    {
        var result = await CallToolAsync("inspect_pixel_asset", new { assetDirectory = directory });
        Assert.False(result.GetProperty("isError").GetBoolean());
        var snapshot = NativeData(result).GetProperty("snapshot");
        return new CreatedPixelAsset(
            snapshot.GetProperty("assetId").GetString()!,
            snapshot.GetProperty("revn").GetInt64(),
            snapshot.GetProperty("sourceDigest").GetString()!,
            layerId);
    }

    /// <summary>
    /// Sends a fully pre-serialized tools/call frame verbatim (no re-serialization), so a test can
    /// craft duplicate members, exact numeric spelling and byte lengths that a typed object cannot
    /// express. This is the only way to drive the original-byte ingress gate end-to-end.
    /// </summary>
    private async Task<JsonElement> SendRawToolCallAsync(string toolName, string argumentsJson)
    {
        var line = "{\"jsonrpc\":\"2.0\",\"id\":" + (++_requestId) +
            ",\"method\":\"tools/call\",\"params\":{\"name\":\"" + toolName + "\",\"arguments\":" + argumentsJson + "}}";
        await _stdin!.WriteLineAsync(line);
        await _stdin.FlushAsync();

        var responseLine = await _stdout!.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30));
        if (string.IsNullOrEmpty(responseLine))
        {
            throw new InvalidOperationException($"No response received for raw {toolName} call");
        }

        using var document = JsonDocument.Parse(responseLine);
        if (document.RootElement.TryGetProperty("error", out var error))
        {
            throw new InvalidOperationException($"Raw tool call transport error: {error}");
        }

        return document.RootElement.GetProperty("result").Clone();
    }

    private static void AssertWireError(JsonElement toolResult, string expectedCode)
    {
        Assert.True(toolResult.GetProperty("isError").GetBoolean());
        Assert.Equal(expectedCode, NativeEnvelope(toolResult).GetProperty("error").GetProperty("code").GetString());
    }

    private static void AssertNoBooleanSubschemas(JsonElement schema, string location)
    {
        Assert.True(schema.ValueKind == JsonValueKind.Object, $"{location} must be a schema object, not {schema.ValueKind}");
        foreach (var keyword in new[] { "properties", "$defs", "definitions", "patternProperties" })
        {
            if (schema.TryGetProperty(keyword, out var map) && map.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in map.EnumerateObject())
                    AssertNoBooleanSubschemas(property.Value, $"{location}.{keyword}.{property.Name}");
            }
        }

        foreach (var keyword in new[] { "anyOf", "oneOf", "allOf", "prefixItems" })
        {
            if (schema.TryGetProperty(keyword, out var list) && list.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var item in list.EnumerateArray())
                    AssertNoBooleanSubschemas(item, $"{location}.{keyword}[{index++}]");
            }
        }

        foreach (var keyword in new[] { "items", "not", "if", "then", "else", "contains", "propertyNames" })
        {
            if (schema.TryGetProperty(keyword, out var subschema))
                AssertNoBooleanSubschemas(subschema, $"{location}.{keyword}");
        }

        // `additionalProperties: false` is the conventional closed-object form and clients accept it.
        if (schema.TryGetProperty("additionalProperties", out var additional) && additional.ValueKind == JsonValueKind.Object)
            AssertNoBooleanSubschemas(additional, $"{location}.additionalProperties");
    }

    private static bool? ReadOnlyHint(JsonElement tool) =>
        tool.TryGetProperty("annotations", out var annotations) && annotations.TryGetProperty("readOnlyHint", out var hint)
            ? hint.GetBoolean()
            : null;

    /// <summary>Reads the shared envelope from a tool result's text block (tolerates a leading image block).</summary>
    private static JsonElement NativeEnvelope(JsonElement toolResult)
    {
        foreach (var block in toolResult.GetProperty("content").EnumerateArray())
        {
            if (block.TryGetProperty("text", out var text))
            {
                return JsonDocument.Parse(text.GetString()!).RootElement.Clone();
            }
        }

        throw new InvalidOperationException("Tool result has no text content block");
    }

    private static JsonElement NativeData(JsonElement toolResult) => NativeEnvelope(toolResult).GetProperty("data");

    private async Task StartServerAsync(string? workingDirectory = null)
    {
        var packagedMcp = Environment.GetEnvironmentVariable("AGD_TEST_MCP_EXECUTABLE");
        if (packagedMcp is not null && (!Path.IsPathFullyQualified(packagedMcp) || !File.Exists(packagedMcp)))
            throw new InvalidOperationException("AGD_TEST_MCP_EXECUTABLE must name an existing absolute executable path.");
        var psi = new ProcessStartInfo
        {
            FileName = packagedMcp ?? "dotnet",
            WorkingDirectory = _testDir,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (packagedMcp is null)
            psi.ArgumentList.Add(Path.Combine(McpProjectPath, "bin", "Release", "net10.0", "agd-mcp.dll"));
        psi.ArgumentList.Add("--working-directory");
        psi.ArgumentList.Add(workingDirectory ?? _testDir);

        _serverProcess = Process.Start(psi);
        if (_serverProcess is null)
        {
            throw new InvalidOperationException("Failed to start MCP server process");
        }

        _serverProcess.BeginErrorReadLine();
        _stdin = _serverProcess.StandardInput;
        _stdout = _serverProcess.StandardOutput;

        await Task.Delay(500);
    }

    private void StopServer()
    {
        if (_serverProcess is not null && !_serverProcess.HasExited)
        {
            try
            {
                _stdin?.Close();
                _serverProcess.Kill(entireProcessTree: true);
                _serverProcess.WaitForExit(5000);
            }
            catch { /* best effort */ }
        }

        _stdin?.Dispose();
        _stdout?.Dispose();
        _serverProcess?.Dispose();
        _stdin = null;
        _stdout = null;
        _serverProcess = null;
    }

    private async Task InitializeAsync()
    {
        await SendRequestAsync("initialize", new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new { name = "test-client", version = "1.0.0" }
        });

        await SendNotificationAsync("notifications/initialized");
    }

    private async Task<JsonDocument> SendRequestAsync(string method, object parameters)
    {
        var request = new
        {
            jsonrpc = "2.0",
            id = ++_requestId,
            method,
            @params = parameters
        };

        var json = JsonSerializer.Serialize(request);
        if (method == "tools/call")
            _toolCalls.Add(JsonSerializer.SerializeToElement(parameters).GetProperty("name").GetString()!);
        await _stdin!.WriteLineAsync(json);
        await _stdin.FlushAsync();

        var responseLine = await _stdout!.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30));
        if (string.IsNullOrEmpty(responseLine))
        {
            throw new InvalidOperationException($"No response received for {method}");
        }

        return JsonDocument.Parse(responseLine);
    }

    private async Task SendNotificationAsync(string method)
    {
        var notification = new
        {
            jsonrpc = "2.0",
            method
        };

        var json = JsonSerializer.Serialize(notification);
        await _stdin!.WriteLineAsync(json);
        await _stdin.FlushAsync();
    }

    private async Task<JsonElement> CallToolAsync(string toolName, object arguments)
    {
        var response = await SendRequestAsync("tools/call", new
        {
            name = toolName,
            arguments
        });

        if (response.RootElement.TryGetProperty("error", out var error))
        {
            throw new InvalidOperationException($"Tool call failed: {error}");
        }

        return response.RootElement.GetProperty("result");
    }

    private static JsonElement ParseToolResult(JsonElement toolResult)
    {
        var content = toolResult.GetProperty("content").EnumerateArray().First();
        var text = content.GetProperty("text").GetString()!;
        var envelope = JsonDocument.Parse(text).RootElement;

        if (envelope.TryGetProperty("ok", out var ok) && ok.GetBoolean())
        {
            return envelope.GetProperty("data");
        }

        return envelope;
    }
}
