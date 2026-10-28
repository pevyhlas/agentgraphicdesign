using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml.Linq;
using FTPlus.AgentGraphicDesign.Core.Assets;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Export;
using FTPlus.AgentGraphicDesign.Core.Platform;
using FTPlus.AgentGraphicDesign.Core.Rendering;
using FTPlus.AgentGraphicDesign.Skia;
using SkiaSharp;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Assets;

public sealed class AssetPipelineTests : IDisposable
{
    private readonly string _directory = FileProbeWorker.FixtureRoot ?? Path.Combine(Path.GetTempPath(), $"agd-assets-{Guid.NewGuid():N}");
    private readonly DocumentEngine _engine = new(renderer: new SkiaRenderer());

    public AssetPipelineTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task MixedRealExportsAndSprite_AreDeterministicAndCheckIsReadOnly()
    {
        var mono = Document("mono");
        _engine.SetColorToken(mono, "ink", "#000000");
        mono.Root.Children[0].Style!.Fill = new Fill { TokenRef = "{ink}" };
        Save("mono.agd.json", mono);
        var color = Document("color", "#f04020");
        _engine.AddNode(color, null, new NewNodeSpec
        {
            Type = "ellipse",
            Name = "accent",
            Width = 8,
            Height = 8,
            Fill = "#2060ff"
        });
        Save("color.agd.json", color);
        var manifest = Manifest("""
            {"version":1,"outputRoot":"generated","defaults":{"profile":"web-icon"},
             "assets":[
               {"source":"mono.agd.json","output":"generated/mono.svg","options":{"currentColor":"#000"}},
               {"source":"color.agd.json","output":"generated/color.svg"},
               {"source":"color.agd.json","output":"generated/nested/color.png","options":{"scale":2,"width":20,"height":12}},
               {"source":"color.agd.json","output":"generated/color.jpg"},
               {"source":"color.agd.json","output":"generated/color.jpeg"},
               {"source":"color.agd.json","output":"generated/color.webp"}],
             "sprites":[{"sources":["color.agd.json","mono.agd.json"],"output":"generated/sprite.svg","options":{"currentColor":"#000"}}]}
            """);
        var pipeline = CreatePipeline(_engine);

        var first = await pipeline.RunAsync(manifest, check: false);

        Assert.True(first.Success);
        Assert.False(first.Check);
        Assert.Equal(7, first.Files.Count);
        Assert.Empty(first.Diagnostics);
        Assert.All(first.Files, file => Assert.Equal("written", file.Status));
        Assert.Equal("generated/mono.svg", first.Files[0].Path);
        Assert.Contains("fill=\"currentColor\"", File.ReadAllText(Full("generated/mono.svg")));
        Assert.Contains("#f04020", File.ReadAllText(Full("generated/color.svg")));
        var sprite = XDocument.Load(Full("generated/sprite.svg"));
        XNamespace svg = "http://www.w3.org/2000/svg";
        Assert.Equal(["color", "mono"], sprite.Descendants(svg + "symbol").Select(symbol => (string?)symbol.Attribute("id")));
        Assert.Contains("currentColor", File.ReadAllText(Full("generated/sprite.svg")));
        foreach (var file in first.Files.Where(file => !file.Path.EndsWith(".svg", StringComparison.Ordinal)))
        {
            using var bitmap = SKBitmap.Decode(Full(file.Path));
            Assert.NotNull(bitmap);
            Assert.Equal(file.Path.EndsWith(".png", StringComparison.Ordinal) ? 40 : 24, bitmap.Width);
            Assert.Equal(24, bitmap.Height);
            Assert.True(bitmap.GetPixel(4, 4).Alpha > 0);
        }
        var snapshot = Snapshot();

        var second = await pipeline.RunAsync(manifest, check: false);
        var check = await pipeline.RunAsync(manifest, check: true);

        Assert.True(second.Success);
        Assert.True(check.Success);
        Assert.True(check.Check);
        Assert.All(second.Files.Concat(check.Files), file => Assert.Equal("unchanged", file.Status));
        Assert.Equal(snapshot, Snapshot());
        Assert.DoesNotContain(Directory.GetFileSystemEntries(_directory, "*", SearchOption.AllDirectories),
            path => path.EndsWith(".lock", StringComparison.Ordinal) || path.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CheckMissingRoot_DoesNotCreateAnything()
    {
        Save("source.json", Document("icon"));
        var manifest = SingleManifest();
        var snapshot = Snapshot();

        var result = await CreatePipeline(_engine).RunAsync(manifest, check: true);

        Assert.False(result.Success);
        Assert.Equal(new AssetFileResult("generated/icon.svg", "missing"), Assert.Single(result.Files));
        Assert.Equal(snapshot, Snapshot());
        Assert.False(Directory.Exists(Full("generated")));
    }

    [Fact]
    public async Task DriftAndCompleteInventory_AreReportedWithoutPruningInEitherMode()
    {
        Save("source.json", Document("icon"));
        var manifest = Manifest("""
            {"version":1,"outputRoot":"generated","assets":[
             {"source":"source.json","output":"generated/missing.svg"},
             {"source":"source.json","output":"generated/stale.svg"}]}
            """);
        Write("generated/stale.svg", "old bytes");
        Write("generated/deep/.orphan", "keep me");
        var snapshot = Snapshot();
        var pipeline = CreatePipeline(_engine);

        var check = await pipeline.RunAsync(manifest, check: true);

        Assert.False(check.Success);
        Assert.Equal(["missing", "stale", "unexpected"], check.Files.Select(file => file.Status));
        Assert.Equal("generated/deep/.orphan", check.Files[2].Path);
        Assert.Equal(snapshot, Snapshot());

        var build = await pipeline.RunAsync(manifest, check: false);

        Assert.False(build.Success);
        Assert.Equal(["written", "written", "unexpected"], build.Files.Select(file => file.Status));
        Assert.Equal("keep me", File.ReadAllText(Full("generated/deep/.orphan")));
        Assert.Equal(["unchanged", "unchanged", "unexpected"],
            (await pipeline.RunAsync(manifest, check: true)).Files.Select(file => file.Status));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LateExportFailure_PreservesAllExistingFilesAndDoesNotCreateDirectories(bool engineError)
    {
        Save("source.json", Document("icon"));
        Write("generated/icon.svg", "previous");
        var manifest = Manifest("""
            {"version":1,"outputRoot":"generated","assets":[
             {"source":"source.json","output":"generated/icon.svg"},
             {"source":"source.json","output":"generated/new/late.svg"}]}
            """);
        var calls = 0;
        var engine = new DocumentEngine(exportAdapters: [new TestExporter((_, _, _) =>
        {
            if (++calls == 2)
            {
                if (engineError)
                    throw new DocumentEngineException(EngineErrors.ResourceLimit, "late failure");
                throw new InvalidOperationException("late failure");
            }
            return "new bytes"u8.ToArray();
        })]);
        var snapshot = Snapshot();

        var error = await Assert.ThrowsAsync<DocumentEngineException>(() => CreatePipeline(engine).RunAsync(manifest, false));

        Assert.Equal(engineError ? EngineErrors.ResourceLimit : "ASSET_EXPORT_FAILED", error.Code);
        Assert.Equal(2, calls);
        Assert.Equal(snapshot, Snapshot());
    }

    [Theory]
    [InlineData("{", EngineErrors.ParseError)]
    [InlineData("[]", EngineErrors.InvalidArguments)]
    [InlineData("{}", EngineErrors.InvalidArguments)]
    [InlineData("{\"version\":2,\"outputRoot\":\"generated\"}", EngineErrors.UnsupportedVersion)]
    [InlineData("{\"version\":1,\"outputRoot\":\"generated\"}", EngineErrors.InvalidArguments)]
    [InlineData("{\"version\":1,\"outputRoot\":\"generated\",\"assets\":null}", EngineErrors.InvalidArguments)]
    [InlineData("{\"version\":1,\"version\":1,\"outputRoot\":\"generated\"}", EngineErrors.InvalidArguments)]
    [InlineData("{\"version\":1,\"outputRoot\":\"generated\",\"hook\":\"anything\"}", EngineErrors.InvalidArguments)]
    [InlineData("{\"version\":1,\"outputRoot\":\"generated\",\"sprites\":[{\"sources\":[],\"output\":\"generated/a.svg\"}]}", EngineErrors.InvalidArguments)]
    public async Task MalformedManifest_HasStableError(string json, string code)
    {
        var error = await Assert.ThrowsAsync<DocumentEngineException>(() => CreatePipeline(_engine).RunAsync(Manifest(json), false));
        Assert.Equal(code, error.Code);
        Assert.False(Directory.Exists(Full("generated")));
    }

    [Theory]
    [InlineData("\"strcit\":true")]
    [InlineData("\"strict\":\"true\"")]
    [InlineData("\"strict\":true,\"strict\":false")]
    [InlineData("\"profile\":\"unknown\"")]
    [InlineData("\"currentColor\":\"red\"")]
    [InlineData("\"width\":1.5")]
    [InlineData("\"width\":0")]
    [InlineData("\"height\":8193")]
    [InlineData("\"scale\":1e1000")]
    [InlineData("\"fontDirs\":[null]")]
    [InlineData("\"allowExternalResources\":true")]
    [InlineData("\"format\":\"png\"")]
    public async Task UnsupportedOrMistypedOptions_AreRejected(string options)
    {
        Save("source.json", Document("icon"));
        var error = await Assert.ThrowsAsync<DocumentEngineException>(() =>
            CreatePipeline(_engine).RunAsync(SingleManifest(options: "{" + options + "}"), false));
        Assert.Equal(EngineErrors.InvalidArguments, error.Code);
    }

    [Theory]
    [InlineData("\"scale\":1")]
    [InlineData("\"width\":24")]
    [InlineData("\"height\":24")]
    [InlineData("\"fontDirs\":[]")]
    [InlineData("\"systemFonts\":false")]
    [InlineData("\"strictFonts\":true")]
    public async Task ExplicitRasterOptionsOnSvg_AreRejectedEvenAtDefaultValues(string option)
    {
        Save("source.json", Document("icon"));
        var error = await Assert.ThrowsAsync<DocumentEngineException>(() =>
            CreatePipeline(_engine).RunAsync(SingleManifest(options: "{" + option + "}"), false));
        Assert.Equal(EngineErrors.InvalidArguments, error.Code);
    }

    [Fact]
    public async Task DefaultsInheritAndExplicitNullClears_WithoutSendingRasterDefaultsToSvg()
    {
        Save("source.json", Document("icon"));
        Directory.CreateDirectory(Full("fonts"));
        var renderOptions = new List<RenderOptions>();
        var engine = new DocumentEngine(renderer: new TestRenderer(options =>
        {
            renderOptions.Add(options);
            return [1, 2, 3];
        }), exportAdapters: [new TestExporter((_, options, _) =>
        {
            Assert.Null(options.CurrentColor);
            Assert.Empty(options.FontDirectories);
            Assert.True(options.SystemFonts);
            Assert.False(options.StrictFonts);
            return "<svg/>"u8.ToArray();
        })]);
        var manifest = Manifest("""
            {"version":1,"outputRoot":"generated",
             "defaults":{"profile":"web-icon","currentColor":"#000","scale":2,"width":40,"height":30,
                         "fontDirs":["fonts"],"systemFonts":true,"strictFonts":false},
             "assets":[
              {"source":"source.json","output":"generated/inherit.png","options":{"currentColor":null,"height":10}},
              {"source":"source.json","output":"generated/clear.webp","options":{
                "currentColor":null,"profile":null,"scale":null,"width":null,"height":null,
                "fontDirs":null,"systemFonts":null,"strictFonts":null}},
              {"source":"source.json","output":"generated/icon.svg","options":{
                "currentColor":null,"scale":null,"width":null,"height":null,
                "fontDirs":null,"systemFonts":null,"strictFonts":null}}]}
            """);

        Assert.True((await CreatePipeline(engine).RunAsync(manifest, false)).Success);

        Assert.Equal(2, renderOptions.Count);
        var inherited = renderOptions[0];
        Assert.Equal(2, inherited.Scale);
        Assert.Equal(40, inherited.Width);
        Assert.Equal(10, inherited.Height);
        Assert.Equal([Full("fonts")], inherited.FontDirectories);
        Assert.True(inherited.SystemFonts);
        Assert.False(inherited.StrictFonts);
        var cleared = renderOptions[1];
        Assert.Equal(1, cleared.Scale);
        Assert.Null(cleared.Width);
        Assert.Null(cleared.Height);
        Assert.Empty(cleared.FontDirectories);
        Assert.False(cleared.SystemFonts);
        Assert.True(cleared.StrictFonts);
    }

    [Fact]
    public async Task ResolvedFontDirectories_AreDeduplicatedInFirstOccurrenceOrder()
    {
        Save("source.json", Document("icon"));
        Directory.CreateDirectory(Full("fonts/first"));
        Directory.CreateDirectory(Full("fonts/second"));
        var calls = 0;
        var engine = new DocumentEngine(renderer: new TestRenderer(options =>
        {
            calls++;
            Assert.Equal([Full("fonts/second"), Full("fonts/first")], options.FontDirectories);
            return [1];
        }));
        var manifest = SingleManifest(output: "generated/icon.png", options: """
            {"fontDirs":["fonts/second","./fonts/second","fonts/first","fonts/second/.","fonts/first"]}
            """);

        Assert.True((await CreatePipeline(engine).RunAsync(manifest, false)).Success);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task RealFontSubstitutionAndSkippedFiles_AreQualifiedNonFatalDiagnosticsInBothModes()
    {
        var document = Document("text");
        _engine.AddNode(document, null, new NewNodeSpec
        {
            Type = "text",
            Text = "Text",
            FontFamily = "AGD-Unavailable-Asset-Test-Font",
            FontSize = 12
        });
        Save("inputs/text.json", document);
        Write("fonts/invalid.ttf", "Not a font");
        var manifest = Manifest("""
            {"version":1,"outputRoot":"generated",
             "defaults":{"strictFonts":false,"fontDirs":["fonts","./fonts","fonts/."]},
             "assets":[
              {"source":"inputs/text.json","output":"generated/text.png"},
              {"source":"inputs/text.json","output":"generated/nested/text.webp"}]}
            """);
        var pipeline = CreatePipeline(_engine);

        var build = await pipeline.RunAsync(manifest, false);
        var snapshot = Snapshot();
        var check = await pipeline.RunAsync(manifest, true);

        Assert.True(build.Success);
        Assert.True(check.Success);
        Assert.Equal(build.Diagnostics.ToArray(), check.Diagnostics.ToArray());
        Assert.Equal(snapshot, Snapshot());
        Assert.All(build.Diagnostics, diagnostic => Assert.Equal("inputs/text.json", diagnostic.Source));
        foreach (var output in new[] { "generated/text.png", "generated/nested/text.webp" })
        {
            var notes = build.Diagnostics.Where(diagnostic => diagnostic.Output == output).ToArray();
            Assert.Contains(notes, diagnostic => diagnostic.Code == RenderDiagnosticCodes.FontSubstituted &&
                diagnostic.Message.Contains("AGD-Unavailable-Asset-Test-Font", StringComparison.Ordinal));
            var skipped = Assert.Single(notes, diagnostic => diagnostic.Code == RenderDiagnosticCodes.FontFileSkipped);
            Assert.Contains("invalid.ttf", skipped.Message);
        }
    }

    [Fact]
    public async Task RendererDiagnostics_HaveBatchAndMessageBoundsWithExplicitTruncation()
    {
        Save("source.json", Document("icon"));
        var rendererDiagnostics = Enumerable.Range(0, AssetPipeline.MaxDiagnostics)
            .Select(_ => new RenderDiagnostic(RenderDiagnosticCodes.FontSubstituted,
                new string('x', AssetPipeline.MaxDiagnosticMessageCharacters + 1))).ToArray();
        var engine = new DocumentEngine(renderer: new TestRenderer(_ => [1], rendererDiagnostics));
        var manifest = Manifest("""
            {"version":1,"outputRoot":"generated","assets":[
             {"source":"source.json","output":"generated/first.png"},
             {"source":"source.json","output":"generated/second.png"}]}
            """);

        var result = await CreatePipeline(engine).RunAsync(manifest, false);

        Assert.True(result.Success);
        Assert.Equal(AssetPipeline.MaxDiagnostics, result.Diagnostics.Count);
        Assert.All(result.Diagnostics, diagnostic =>
        {
            Assert.Equal("source.json", diagnostic.Source);
            Assert.Equal("generated/first.png", diagnostic.Output);
            Assert.True(diagnostic.Message.Length <= AssetPipeline.MaxDiagnosticMessageCharacters);
        });
        Assert.EndsWith("... [truncated]", result.Diagnostics[0].Message);
        Assert.Equal(RenderDiagnosticCodes.DiagnosticsTruncated, result.Diagnostics[^1].Code);
    }

    [Fact]
    public async Task ValidationAlwaysRuns_StrictWarningsFailByDefault()
    {
        var document = Document("icon");
        document.Root.Children = [new PathNode { D = "" }];
        Save("source.json", document);
        var pipeline = CreatePipeline(_engine);

        var warning = await Assert.ThrowsAsync<DocumentEngineException>(() => pipeline.RunAsync(SingleManifest(), false));
        Assert.Equal("PATH001", warning.Code);
        Assert.True((await pipeline.RunAsync(SingleManifest(options: "{\"strict\":false}"), false)).Success);

        document.Canvas.Width = 0;
        Save("source.json", document);
        var error = await Assert.ThrowsAsync<DocumentEngineException>(() =>
            pipeline.RunAsync(SingleManifest(options: "{\"strict\":false}"), false));
        Assert.Equal("CANVAS001", error.Code);
    }

    [Fact]
    public async Task ProfileCanBeClearedForText_AndMissingRasterFontsFailByDefault()
    {
        var document = Document("icon");
        _engine.AddNode(document, null, new NewNodeSpec
        {
            Type = "text",
            Text = "Text",
            FontFamily = "AGD-Unavailable-Asset-Test-Font",
            FontSize = 12
        });
        Save("source.json", document);
        var pipeline = CreatePipeline(_engine);
        var profile = await Assert.ThrowsAsync<DocumentEngineException>(() =>
            pipeline.RunAsync(SingleManifest(options: "{\"profile\":\"web-icon\",\"strict\":false}"), false));
        Assert.Equal("WEBICON002", profile.Code);
        var fonts = await Assert.ThrowsAsync<DocumentEngineException>(() =>
            pipeline.RunAsync(SingleManifest(output: "generated/icon.png"), false));
        Assert.Equal(EngineErrors.FontNotFound, fonts.Code);
        var manifest = Manifest("""
            {"version":1,"outputRoot":"generated","defaults":{"profile":"web-icon"},"assets":[
             {"source":"source.json","output":"generated/icon.svg","options":{"profile":null}}]}
            """);
        Assert.True((await pipeline.RunAsync(manifest, false)).Success);
    }

    [Theory]
    [InlineData("../outside.json", "generated/icon.svg", "generated")]
    [InlineData("/etc/passwd", "generated/icon.svg", "generated")]
    [InlineData("source.json", "../icon.svg", "generated")]
    [InlineData("source.json", "generated-sibling/icon.svg", "generated")]
    [InlineData("source.json", "generated/icon.svg", ".")]
    [InlineData("source.json", "generated/icon.svg", "../generated")]
    [InlineData("generated/source.json", "generated/icon.svg", "generated")]
    [InlineData("source.json", "generated/icon.svg", "source.json")]
    [InlineData("source.json", "generated/icon.svg:stream", "generated")]
    [InlineData("source.json", "generated/../generated/icon.svg", "generated")]
    [InlineData("source.json", "generated/NUL.svg", "generated")]
    [InlineData("source.json", "generated/COM1.svg", "generated")]
    public async Task UnsafePaths_AreRejected(string source, string output, string root)
    {
        Save("source.json", Document("icon"));
        var manifest = Manifest(JsonSerializer.Serialize(new
        {
            version = 1,
            outputRoot = root,
            assets = new[] { new { source, output } }
        }));
        var error = await Assert.ThrowsAsync<DocumentEngineException>(() => CreatePipeline(_engine).RunAsync(manifest, false));
        Assert.Equal(EngineErrors.PathEscape, error.Code);
    }

    [Theory]
    [InlineData("generated/icon.svg", "generated/icon.svg")]
    [InlineData("generated/icon.svg", "generated/ICON.SVG")]
    [InlineData("generated/a/icon.svg", "generated/A/other.svg")]
    [InlineData("generated/icon.svg", "generated/icon.svg/child.svg")]
    public async Task OutputCollisions_AreRejectedBeforeExport(string first, string second)
    {
        Save("source.json", Document("icon"));
        var manifest = Manifest(JsonSerializer.Serialize(new
        {
            version = 1,
            outputRoot = "generated",
            assets = new[]
            {
                new { source = "source.json", output = first }, new { source = "source.json", output = second }
            }
        }));
        var error = await Assert.ThrowsAsync<DocumentEngineException>(() => CreatePipeline(_engine).RunAsync(manifest, false));
        Assert.Equal(EngineErrors.InvalidArguments, error.Code);
        Assert.False(Directory.Exists(Full("generated")));
    }

    [Theory]
    [InlineData("manifest")]
    [InlineData("source")]
    [InlineData("source-parent")]
    [InlineData("root")]
    [InlineData("output-parent")]
    [InlineData("output")]
    [InlineData("orphan")]
    [InlineData("font-directory")]
    [InlineData("font-file")]
    public async Task SymlinksIncludingDanglingLeaves_AreRejected(string location)
    {
        if (OperatingSystem.IsWindows())
            return; // Creating links requires privileges outside normal Windows test accounts.
        Save("source.json", Document("icon"));
        var manifest = SingleManifest();
        switch (location)
        {
            case "manifest":
                File.CreateSymbolicLink(Full("linked.json"), manifest);
                manifest = Full("linked.json");
                break;
            case "source":
                File.Delete(Full("source.json"));
                File.CreateSymbolicLink(Full("source.json"), Full("missing.json"));
                break;
            case "source-parent":
                Directory.CreateSymbolicLink(Full("linked"), _directory);
                manifest = SingleManifest(source: "linked/source.json");
                break;
            case "root":
                Directory.CreateSymbolicLink(Full("generated"), Full("missing-directory"));
                break;
            case "output-parent":
                Directory.CreateDirectory(Full("generated"));
                Directory.CreateSymbolicLink(Full("generated/sub"), _directory);
                manifest = SingleManifest(output: "generated/sub/icon.svg");
                break;
            case "output":
            case "orphan":
                Directory.CreateDirectory(Full("generated"));
                File.CreateSymbolicLink(Full(location == "output" ? "generated/icon.svg" : "generated/orphan"), Full("missing"));
                break;
            case "font-directory":
                Directory.CreateSymbolicLink(Full("fonts"), _directory);
                manifest = SingleManifest(output: "generated/icon.png", options: "{\"fontDirs\":[\"fonts\"]}");
                break;
            case "font-file":
                Directory.CreateDirectory(Full("fonts"));
                File.CreateSymbolicLink(Full("fonts/dangling.ttf"), Full("missing.ttf"));
                manifest = SingleManifest(output: "generated/icon.png", options: "{\"fontDirs\":[\"fonts\"]}");
                break;
        }

        foreach (var check in new[] { true, false })
        {
            var error = await Assert.ThrowsAsync<DocumentEngineException>(() => CreatePipeline(_engine).RunAsync(manifest, check));
            Assert.Equal(EngineErrors.PathEscape, error.Code);
        }
    }

    [Theory]
    [InlineData("source.json")]
    [InlineData("generated/icon.svg")]
    [InlineData("generated/orphan")]
    [InlineData("manifest.json")]
    public async Task UnixFifos_AreRejectedWithoutOpening(string relative)
    {
        if (OperatingSystem.IsWindows())
            return;
        if (!FileProbeWorker.IsChild)
        {
            await FileProbeWorker.RunAsync("pipeline-fifo", relative);
            return;
        }
        Save("source.json", Document("icon"));
        var manifest = SingleManifest();
        Directory.CreateDirectory(Path.GetDirectoryName(Full(relative))!);
        File.Delete(Full(relative));
        Assert.Equal(0, MakeFifo(Full(relative), 0x180));

        var error = await Assert.ThrowsAsync<DocumentEngineException>(() => CreatePipeline(_engine).RunAsync(manifest, true));

        Assert.Equal(EngineErrors.PathEscape, error.Code);
    }

    [Fact]
    public async Task DirectoryAtOutputAndSource_IsRejected()
    {
        Save("source.json", Document("icon"));
        Directory.CreateDirectory(Full("generated/icon.svg"));
        var pipeline = CreatePipeline(_engine);
        var output = await Assert.ThrowsAsync<DocumentEngineException>(() => pipeline.RunAsync(SingleManifest(), false));
        Assert.Equal(EngineErrors.PathEscape, output.Code);
        Directory.Delete(Full("generated"), true);
        File.Delete(Full("source.json"));
        Directory.CreateDirectory(Full("source.json"));
        var source = await Assert.ThrowsAsync<DocumentEngineException>(() => pipeline.RunAsync(SingleManifest(), true));
        Assert.Equal(EngineErrors.PathEscape, source.Code);
    }

    [Fact]
    public async Task UnixSocket_IsRejectedWithoutOpening()
    {
        if (OperatingSystem.IsWindows())
            return;
        if (!FileProbeWorker.IsChild)
        {
            await FileProbeWorker.RunAsync("pipeline-socket");
            return;
        }
        using var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.Unix,
            System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Unspecified);
        socket.Bind(new System.Net.Sockets.UnixDomainSocketEndPoint(Full("s")));

        var error = await Assert.ThrowsAsync<DocumentEngineException>(() =>
            CreatePipeline(_engine).RunAsync(SingleManifest(source: "s"), true));

        Assert.Equal(EngineErrors.PathEscape, error.Code);
    }

    [Theory]
    [InlineData("missing.json", "generated/icon.svg", "{}", EngineErrors.DocumentNotFound)]
    [InlineData("source.json", "generated/icon.gif", "{}", EngineErrors.ExportUnsupported)]
    [InlineData("source.json", "generated/icon.png", "{\"currentColor\":\"#000\"}", EngineErrors.InvalidArguments)]
    [InlineData("source.json", "generated/icon.png", "{\"fontDirs\":[\"generated\"]}", EngineErrors.PathEscape)]
    [InlineData("source.json", "generated/icon.png", "{\"fontDirs\":[\"../fonts\"]}", EngineErrors.PathEscape)]
    [InlineData("source.json", "generated/icon.png", "{\"scale\":4,\"width\":4096}", EngineErrors.InvalidDimensions)]
    public async Task PlanningAndSizingErrors_AreStructured(string source, string output, string options, string code)
    {
        Save("source.json", Document("icon"));
        var error = await Assert.ThrowsAsync<DocumentEngineException>(() =>
            CreatePipeline(_engine).RunAsync(SingleManifest(source, output, options), false));
        Assert.Equal(code, error.Code);
        Assert.False(Directory.Exists(Full("generated")));
    }

    [Fact]
    public async Task InvalidDocument_AndUnknownEntryPropertiesFailBeforePublication()
    {
        Write("source.json", "{}");
        var pipeline = CreatePipeline(_engine);
        var parse = await Assert.ThrowsAsync<DocumentEngineException>(() => pipeline.RunAsync(SingleManifest(), false));
        Assert.Equal(EngineErrors.ParseError, parse.Code);
        var manifest = Manifest("""
            {"version":1,"outputRoot":"generated","assets":[
             {"source":"source.json","output":"generated/icon.svg","sources":[]}]}
            """);
        var property = await Assert.ThrowsAsync<DocumentEngineException>(() => pipeline.RunAsync(manifest, false));
        Assert.Equal(EngineErrors.InvalidArguments, property.Code);
        Assert.False(Directory.Exists(Full("generated")));
    }

    [Fact]
    public async Task PreCancelledAndMidExportCancellation_NeverPublish()
    {
        Save("source.json", Document("icon"));
        var manifest = SingleManifest();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreatePipeline(_engine).RunAsync(manifest, false, cancelled.Token));
        using var duringExport = new CancellationTokenSource();
        var engine = new DocumentEngine(exportAdapters: [new TestExporter((_, _, ct) =>
        {
            Assert.Equal(duringExport.Token, ct);
            duringExport.Cancel();
            return [1];
        })]);
        var snapshot = Snapshot();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreatePipeline(engine).RunAsync(manifest, false, duringExport.Token));

        Assert.Equal(snapshot, Snapshot());
    }

    [Fact]
    public async Task CheckWorksOnReadOnlyManifestAndOutputDirectories()
    {
        if (OperatingSystem.IsWindows())
            return;
        Save("source.json", Document("icon"));
        var manifest = SingleManifest();
        var pipeline = CreatePipeline(_engine);
        await pipeline.RunAsync(manifest, false);
        var directories = new[] { _directory, Full("generated") };
        var files = Directory.GetFiles(_directory, "*", SearchOption.AllDirectories);
        try
        {
            foreach (var file in files)
                File.SetUnixFileMode(file, UnixFileMode.UserRead);
            foreach (var directory in directories)
                File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            var snapshot = Snapshot();

            Assert.True((await pipeline.RunAsync(manifest, true)).Success);

            Assert.Equal(snapshot, Snapshot());
        }
        finally
        {
            foreach (var directory in directories)
                File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            foreach (var file in files)
                File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public async Task ManifestAndBatchInputLimits_AreEnforcedBeforeLoading()
    {
        var manifest = Manifest(new string(' ', AssetPipeline.MaxManifestBytes + 1));
        var pipeline = CreatePipeline(_engine);
        Assert.Equal(EngineErrors.ResourceLimit,
            (await Assert.ThrowsAsync<DocumentEngineException>(() => pipeline.RunAsync(manifest, true))).Code);

        manifest = Manifest(JsonSerializer.Serialize(new
        {
            version = 1,
            outputRoot = "generated",
            sprites = new[]
            {
                new { sources = Enumerable.Repeat("source.json", 257).ToArray(), output = "generated/sprite.svg" }
            }
        }));
        Assert.Equal(EngineErrors.ResourceLimit,
            (await Assert.ThrowsAsync<DocumentEngineException>(() => pipeline.RunAsync(manifest, true))).Code);

        var entries = Enumerable.Range(0, 5).Select(i => new { source = $"source{i}.json", output = $"generated/{i}.svg" }).ToArray();
        foreach (var entry in entries)
        {
            using var file = File.Create(Full(entry.source));
            file.SetLength(16 * 1024 * 1024);
        }
        manifest = Manifest(JsonSerializer.Serialize(new { version = 1, outputRoot = "generated", assets = entries }));
        Assert.Equal(EngineErrors.ResourceLimit,
            (await Assert.ThrowsAsync<DocumentEngineException>(() => pipeline.RunAsync(manifest, false))).Code);
        Assert.False(Directory.Exists(Full("generated")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PerOutputAndAggregateOutputLimits_PreventPublication(bool individual)
    {
        Save("source.json", Document("icon"));
        var data = new byte[(individual ? 33 : 24) * 1024 * 1024];
        var engine = new DocumentEngine(exportAdapters: [new TestExporter((_, _, _) => data)]);
        var manifest = Manifest(JsonSerializer.Serialize(new
        {
            version = 1,
            outputRoot = "generated",
            assets = Enumerable.Range(0, individual ? 1 : 3)
                .Select(i => new { source = "source.json", output = $"generated/{i}.svg" }).ToArray()
        }));

        var error = await Assert.ThrowsAsync<DocumentEngineException>(() => CreatePipeline(engine).RunAsync(manifest, false));

        Assert.Equal(EngineErrors.ResourceLimit, error.Code);
        Assert.False(Directory.Exists(Full("generated")));
    }

    [Fact]
    public async Task AggregateRasterPixelLimit_PreventsSecondRenderAndPublication()
    {
        Save("source.json", Document("icon"));
        var renders = 0;
        var engine = new DocumentEngine(renderer: new TestRenderer(_ => { renders++; return [1]; }));
        var manifest = Manifest("""
            {"version":1,"outputRoot":"generated","defaults":{"width":6000,"height":6000},"assets":[
             {"source":"source.json","output":"generated/a.png"},
             {"source":"source.json","output":"generated/b.png"}]}
            """);

        var error = await Assert.ThrowsAsync<DocumentEngineException>(() => CreatePipeline(engine).RunAsync(manifest, false));

        Assert.Equal(EngineErrors.ResourceLimit, error.Code);
        Assert.Equal(1, renders);
        Assert.False(Directory.Exists(Full("generated")));
    }

    [Fact]
    public async Task InventoryIsBounded_IncludingUnexpectedEmptyDirectories()
    {
        Save("source.json", Document("icon"));
        var manifest = SingleManifest();
        for (var index = 0; index < 4097; index++)
            Directory.CreateDirectory(Full($"generated/{index}"));

        var error = await Assert.ThrowsAsync<DocumentEngineException>(() => CreatePipeline(_engine).RunAsync(manifest, true));

        Assert.Equal(EngineErrors.ResourceLimit, error.Code);
        Assert.False(File.Exists(Full("generated/icon.svg")));
    }

    [Theory]
    [InlineData("generated/ICON.SVG", "generated/icon.svg", "generated")]
    [InlineData("generated/Icons/unrelated.svg", "generated/icons/icon.svg", "generated")]
    [InlineData("Generated/unrelated.svg", "generated/icon.svg", "generated")]
    [InlineData("Build/generated/unrelated.svg", "build/generated/icon.svg", "build/generated")]
    public async Task OnDiskCaseAliasesIncludingParents_AreRejectedBeforeAnyPublication(string existing, string output, string outputRoot)
    {
        Save("source.json", Document("icon"));
        Write(existing, "unowned");
        var manifest = Manifest(JsonSerializer.Serialize(new
        {
            version = 1,
            outputRoot,
            assets = new[]
            {
                new { source = "source.json", output = outputRoot + "/first.svg" },
                new { source = "source.json", output }
            }
        }));
        var snapshot = Snapshot();

        foreach (var check in new[] { true, false })
        {
            var error = await Assert.ThrowsAsync<DocumentEngineException>(() => CreatePipeline(_engine).RunAsync(manifest, check));
            Assert.Equal(EngineErrors.InvalidArguments, error.Code);
            Assert.Contains("case alias", error.Message);
            Assert.Equal(snapshot, Snapshot());
            Assert.Equal("unowned", File.ReadAllText(Full(existing)));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CaseAliasesIntroducedDuringGeneration_AreRejectedBeforeAnyPublication(bool check)
    {
        Save("source.json", Document("icon"));
        Write("generated/first.svg", "previous");
        var manifest = Manifest("""
            {"version":1,"outputRoot":"generated","assets":[
             {"source":"source.json","output":"generated/first.svg"},
             {"source":"source.json","output":"generated/icon.svg"}]}
            """);
        string[]? snapshot = null;
        var calls = 0;
        var engine = new DocumentEngine(exportAdapters: [new TestExporter((_, _, _) =>
        {
            if (++calls == 2)
            {
                Write("generated/ICON.SVG", "unowned");
                snapshot = Snapshot();
            }
            return [1];
        })]);

        var error = await Assert.ThrowsAsync<DocumentEngineException>(() => CreatePipeline(engine).RunAsync(manifest, check));

        Assert.Equal(EngineErrors.InvalidArguments, error.Code);
        Assert.Equal(2, calls);
        Assert.Equal(snapshot, Snapshot());
        Assert.Equal("previous", File.ReadAllText(Full("generated/first.svg")));
    }

    [Fact]
    public async Task PlatformGuard_FollowsTheRegistryGate()
    {
        Save("source.json", Document("icon"));
        var manifest = SingleManifest();
        var pipeline = CreatePipeline(_engine);
        var platform = PlatformProfile.Current;
        if (platform.GateAdmits(PlatformCapability.AssetPipeline))
        {
            Assert.True((await pipeline.RunAsync(manifest, false)).Success);
        }
        else
        {
            var error = await Assert.ThrowsAsync<DocumentEngineException>(() => pipeline.RunAsync(manifest, false));
            Assert.Equal("ASSET_PLATFORM_UNSUPPORTED", error.Code);
            Assert.Equal(platform.RefusalMessage(PlatformCapability.AssetPipeline), error.Message);
            Assert.False(Directory.Exists(Full("generated")));
        }
    }

    [Theory]
    [InlineData("generated/icon.rgba8")]
    [InlineData("generated/icon.rgba")]
    [InlineData("generated/icon.raw")]
    public async Task RawRgbaOutputs_AreRefusedAndNothingIsWritten(string output)
    {
        // Feature 012 AC-02: the internal Rgba8 render buffer is never a manifest output format.
        Save("source.json", Document("icon"));
        var manifest = SingleManifest(output: output);
        var pipeline = CreatePipeline(_engine);

        var error = await Assert.ThrowsAsync<DocumentEngineException>(() => pipeline.RunAsync(manifest, false));

        var admitted = PlatformProfile.Current.GateAdmits(PlatformCapability.AssetPipeline);
        Assert.Equal(admitted ? EngineErrors.ExportUnsupported : "ASSET_PLATFORM_UNSUPPORTED", error.Code);
        Assert.False(Directory.Exists(Full("generated")));
        // No manifest format maps to the raw buffer: VectorAssetFormat gains no member (plan C1).
        Assert.Equal(["Svg", "Png", "Jpeg", "Webp"], Enum.GetNames<VectorAssetFormat>());
    }

    [Theory]
    [InlineData("invalid name", "generated/sprite.svg", EngineErrors.InvalidProperty)]
    [InlineData("icon", "generated/sprite.png", EngineErrors.InvalidArguments)]
    public async Task SpriteNameAndFormatErrors_AreStructured(string name, string output, string code)
    {
        Save("source.json", Document(name));
        var manifest = Manifest(JsonSerializer.Serialize(new
        {
            version = 1,
            outputRoot = "generated",
            sprites = new[] { new { sources = new[] { "source.json" }, output } }
        }));

        var error = await Assert.ThrowsAsync<DocumentEngineException>(() => CreatePipeline(_engine).RunAsync(manifest, false));

        Assert.Equal(code, error.Code);
        Assert.False(Directory.Exists(Full("generated")));
    }

    [Fact]
    public async Task DuplicateRealSpriteDefinitionsAndDocumentNames_AreRejected()
    {
        var first = Document("first");
        var rectangle = first.Root.Children[0];
        rectangle.Style!.Fill = new Fill
        {
            Gradient = new LinearGradient
            {
                Stops = [new GradientStop { Offset = 0, Color = "#000" }, new GradientStop { Offset = 1, Color = "#fff" }]
            }
        };
        Save("first.json", first);
        first.Name = "second";
        Save("second.json", first);
        var manifest = Manifest("""
            {"version":1,"outputRoot":"generated","sprites":[
             {"sources":["first.json","second.json"],"output":"generated/sprite.svg"}]}
            """);
        var pipeline = CreatePipeline(_engine);
        var definitions = await Assert.ThrowsAsync<DocumentEngineException>(() => pipeline.RunAsync(manifest, false));
        Assert.Equal(EngineErrors.InvalidArguments, definitions.Code);
        Assert.Contains("grad-", definitions.Message);

        Save("second.json", Document("first"));
        var symbols = await Assert.ThrowsAsync<DocumentEngineException>(() => pipeline.RunAsync(manifest, false));
        Assert.Equal(EngineErrors.InvalidArguments, symbols.Code);
        Assert.Contains("'first'", symbols.Message);
        Assert.False(Directory.Exists(Full("generated")));
    }

    private static AssetPipeline CreatePipeline(IDocumentEngine engine) =>
        new AssetPipeline(engine, new VectorAssetProducer(engine));

    private Document Document(string name, string fill = "#000000")
    {
        var document = _engine.CreateDocument(name, 24, 24);
        _engine.AddNode(document, null, new NewNodeSpec
        {
            Type = "rectangle",
            Name = "paint",
            Width = 24,
            Height = 24,
            Fill = fill
        });
        return document;
    }

    private string Full(string relative) => Path.Combine(_directory, relative);
    private void Save(string relative, Document document) => Write(relative, _engine.ToJson(document));
    private void Write(string relative, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Full(relative))!);
        File.WriteAllText(Full(relative), text);
    }

    private string Manifest(string json)
    {
        Write("manifest.json", json);
        return Full("manifest.json");
    }

    private string SingleManifest(string source = "source.json", string output = "generated/icon.svg", string options = "{}") =>
        Manifest($$"""
            {"version":1,"outputRoot":"generated","assets":[{"source":"{{source}}","output":"{{output}}","options":{{options}}}]}
            """);

    private string[] Snapshot() => Directory.GetFileSystemEntries(_directory, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal)
        .Select(path => Directory.Exists(path) ? $"directory:{path}:{Directory.GetLastWriteTimeUtc(path).Ticks}"
            : $"file:{path}:{File.GetLastWriteTimeUtc(path).Ticks}:{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))}")
        .ToArray();

    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MakeFifo([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mode);

    private sealed class TestExporter(Func<Document, ExportOptions, CancellationToken, byte[]> export) : IExportAdapter
    {
        public string Id => "svg";
        public ExportCapabilities Capabilities => ExportCapabilities.Vector;
        public Task<ExportResult> ExportAsync(Document document, ExportOptions options, CancellationToken ct = default) =>
            Task.FromResult(new ExportResult { Data = export(document, options, ct), MimeType = "image/svg+xml" });
    }

    private sealed class TestRenderer(Func<RenderOptions, byte[]> render, IReadOnlyList<RenderDiagnostic>? diagnostics = null) : IRenderer
    {
        public RenderResult Render(Document document, RenderOptions options) =>
            new() { Data = render(options), MimeType = "image/png", Diagnostics = diagnostics ?? [] };
    }
}
