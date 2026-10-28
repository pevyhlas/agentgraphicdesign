using System.Buffers;
using System.Collections;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using FTPlus.AgentGraphicDesign.Core.Assets;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Persistence;
using FTPlus.AgentGraphicDesign.Core.Paths;
using FTPlus.AgentGraphicDesign.Core.Rendering;
using FTPlus.AgentGraphicDesign.Core.Validation;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Assets;

public sealed class AssetProducerPipelineTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"agd-producers-{Guid.NewGuid():N}");
    private readonly DocumentEngine _engine = new();

    public AssetProducerPipelineTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task Requests_MapFormatsAndDefaults_InAssetThenSpriteOccurrenceOrder()
    {
        Save("inputs/first.json", Document("first"));
        Save("inputs/second.json", Document("second"));
        var manifest = Manifest("""
            {"version":1,"outputRoot":"generated",
             "sprites":[{"sources":["inputs/second.json","inputs/first.json"],"output":"generated/sprite.svg"}],
             "assets":[
              {"source":"inputs/first.json","output":"generated/icon.svg"},
              {"source":"inputs/second.json","output":"generated/icon.png"},
              {"source":"inputs/first.json","output":"generated/icon.jpg"},
              {"source":"inputs/second.json","output":"generated/icon.jpeg"},
              {"source":"inputs/first.json","output":"generated/icon.webp"}]}
            """);
        var formats = new[] { VectorAssetFormat.Svg, VectorAssetFormat.Png, VectorAssetFormat.Jpeg,
            VectorAssetFormat.Jpeg, VectorAssetFormat.Webp, VectorAssetFormat.Svg, VectorAssetFormat.Svg };
        var names = new[] { "first", "second", "first", "second", "first", "second", "first" };
        var calls = 0;
        var producer = new TestProducer((request, _, _) =>
        {
            var index = calls++;
            Assert.Equal(formats[index], request.Format);
            Assert.Equal(names[index], request.Document.Name);
            Assert.Equal(PlatformPaths.FullPath(Full($"inputs/{names[index]}.json")), request.SourceName);
            Assert.Equal(index >= 5, request.AsSymbol);
            Assert.True(request.Strict);
            Assert.Equal(ValidationProfile.Default, request.Profile);
            Assert.Null(request.CurrentColor);
            Assert.Equal(1, request.Scale);
            Assert.Null(request.Width);
            Assert.Null(request.Height);
            Assert.False(request.FontDirectories.IsDefault);
            Assert.Empty(request.FontDirectories);
            Assert.False(request.SystemFonts);
            Assert.True(request.StrictFonts);
            return Task.FromResult(new AssetProducerResult(Encoding.UTF8.GetBytes(request.AsSymbol
                ? $"<symbol id=\"{request.Document.Name}\"/>" : "opaque producer bytes"), []));
        });

        var result = await new AssetPipeline(_engine, producer).RunAsync(manifest, false);

        Assert.True(result.Success);
        Assert.Equal(7, calls);
        Assert.Equal(["generated/icon.svg", "generated/icon.png", "generated/icon.jpg",
            "generated/icon.jpeg", "generated/icon.webp", "generated/sprite.svg"], result.Files.Select(file => file.Path));
        XNamespace svg = "http://www.w3.org/2000/svg";
        Assert.Equal(["second", "first"], XDocument.Load(Full("generated/sprite.svg"))
            .Descendants(svg + "symbol").Select(symbol => (string?)symbol.Attribute("id")));
    }

    [Fact]
    public async Task Requests_MapInheritedAndClearedOptions_WithImmutableOrderedFonts()
    {
        Save("source.json", Document("icon"));
        Directory.CreateDirectory(Full("fonts/first"));
        Directory.CreateDirectory(Full("fonts/second"));
        var manifest = Manifest("""
            {"version":1,"outputRoot":"generated",
             "defaults":{"strict":false,"profile":"web-icon","currentColor":"#000","scale":2,"width":40,"height":30,
                         "fontDirs":["fonts/second","./fonts/second","fonts/first","fonts/second/."],
                         "systemFonts":true,"strictFonts":false},
             "assets":[
              {"source":"source.json","output":"generated/inherited.png","options":{"currentColor":null,"height":10}},
              {"source":"source.json","output":"generated/cleared.webp","options":{
               "strict":null,"profile":null,"currentColor":null,"scale":null,"width":null,"height":null,
               "fontDirs":null,"systemFonts":null,"strictFonts":null}},
              {"source":"source.json","output":"generated/icon.svg","options":{
               "strict":true,"scale":null,"width":null,"height":null,"fontDirs":null,"systemFonts":null,"strictFonts":null}}]}
            """);
        ImmutableArray<string> acceptedFonts = [];
        var calls = 0;
        var producer = new TestProducer((request, _, _) =>
        {
            switch (calls++)
            {
                case 0:
                    Assert.Equal(VectorAssetFormat.Png, request.Format);
                    Assert.False(request.Strict);
                    Assert.Equal(ValidationProfile.WebIcon, request.Profile);
                    Assert.Null(request.CurrentColor);
                    Assert.Equal(2, request.Scale);
                    Assert.Equal(40, request.Width);
                    Assert.Equal(10, request.Height);
                    acceptedFonts = request.FontDirectories;
                    Assert.Equal([PlatformPaths.FullPath(Full("fonts/second")), PlatformPaths.FullPath(Full("fonts/first"))], acceptedFonts.ToArray());
                    var list = (IList<string>)request.FontDirectories;
                    Assert.True(list.IsReadOnly);
                    Assert.Throws<NotSupportedException>(() => list[0] = "changed");
                    Assert.True(request.SystemFonts);
                    Assert.False(request.StrictFonts);
                    break;
                case 1:
                    Assert.Equal(VectorAssetFormat.Webp, request.Format);
                    Assert.True(request.Strict);
                    Assert.Equal(ValidationProfile.Default, request.Profile);
                    Assert.Null(request.CurrentColor);
                    Assert.Equal(1, request.Scale);
                    Assert.Null(request.Width);
                    Assert.Null(request.Height);
                    Assert.Empty(request.FontDirectories);
                    Assert.False(request.SystemFonts);
                    Assert.True(request.StrictFonts);
                    break;
                case 2:
                    Assert.Equal(VectorAssetFormat.Svg, request.Format);
                    Assert.True(request.Strict);
                    Assert.Equal(ValidationProfile.WebIcon, request.Profile);
                    Assert.Equal("#000", request.CurrentColor);
                    Assert.Equal(1, request.Scale);
                    Assert.Null(request.Width);
                    Assert.Null(request.Height);
                    Assert.Empty(request.FontDirectories);
                    break;
                default:
                    Assert.Fail("Unexpected producer call");
                    break;
            }
            return Task.FromResult(new AssetProducerResult(new byte[] { 1 }, []));
        });

        Assert.True((await new AssetPipeline(_engine, producer).RunAsync(manifest, false)).Success);
        Assert.Equal(3, calls);
        Assert.Equal([PlatformPaths.FullPath(Full("fonts/second")), PlatformPaths.FullPath(Full("fonts/first"))], acceptedFonts.ToArray());
    }

    [Fact]
    public async Task EachOccurrence_LoadsJustInTime_WithoutCachingOrRoundingThroughASerializationClone()
    {
        Save("first.json", Document("first"));
        Save("later.json", Document("before"));
        var changed = JsonNode.Parse(_engine.ToJson(Document("after")))!;
        const double preciseWidth = 24.123456789;
        changed["canvas"]!["width"] = preciseWidth;
        var manifest = Manifest("""
            {"version":1,"outputRoot":"generated","assets":[
             {"source":"first.json","output":"generated/first.svg"},
             {"source":"later.json","output":"generated/later.svg"},
             {"source":"later.json","output":"generated/repeated.svg"}]}
            """);
        var calls = 0;
        var producer = new TestProducer((request, _, _) =>
        {
            if (calls++ == 0)
            {
                Assert.Equal("first", request.Document.Name);
                Write("later.json", changed.ToJsonString());
            }
            else
            {
                Assert.Equal("after", request.Document.Name);
                Assert.Equal(preciseWidth, request.Document.Canvas.Width);
                // Mutating this borrowed occurrence must not affect a subsequent load of the same path.
                request.Document.Name = "only-this-occurrence";
            }
            return Task.FromResult(new AssetProducerResult(new byte[] { (byte)calls }, []));
        });

        Assert.True((await new AssetPipeline(_engine, producer).RunAsync(manifest, false)).Success);
        Assert.Equal(3, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceGrowingAfterPlanning_IsALateParseError_WithoutPublication(bool check)
    {
        Save("first.json", Document("first"));
        Save("later.json", Document("later"));
        Write("generated/first.svg", "previous");
        var manifest = Manifest("""
            {"version":1,"outputRoot":"generated","assets":[
             {"source":"first.json","output":"generated/first.svg"},
             {"source":"later.json","output":"generated/new/later.svg"}]}
            """);
        string[]? afterGrowth = null;
        var calls = 0;
        var producer = new TestProducer((_, _, _) =>
        {
            calls++;
            using (var file = File.OpenWrite(Full("later.json")))
                file.SetLength(FileDocumentStore.DefaultMaxDocumentBytes + 1);
            afterGrowth = Snapshot();
            return Task.FromResult(new AssetProducerResult("new bytes"u8.ToArray(), []));
        });

        var error = await Assert.ThrowsAsync<DocumentEngineException>(() =>
            new AssetPipeline(_engine, producer).RunAsync(manifest, check));

        Assert.Equal(EngineErrors.ParseError, error.Code);
        Assert.Contains(Full("later.json"), error.Message);
        Assert.Equal(1, calls);
        Assert.NotNull(afterGrowth);
        Assert.Equal(afterGrowth, Snapshot());
    }

    [Fact]
    public async Task AcceptedStandaloneSlicesAndDiagnostics_AreFrozenBeforeBufferAndListReuse()
    {
        Save("inputs/first.json", Document("first"));
        Save("inputs/later.json", Document("later"));
        var manifest = Manifest("""
            {"version":1,"outputRoot":"generated","assets":[
             {"source":"inputs/first.json","output":"generated/first.svg"},
             {"source":"inputs/later.json","output":"generated/later.png"}]}
            """);
        var buffer = new byte[] { 99, 0, 0, 0, 98 };
        var diagnostics = new List<RenderDiagnostic>();
        var calls = 0;
        var producer = new TestProducer((_, _, _) =>
        {
            var first = calls++ % 2 == 0;
            buffer.AsSpan(1, 3).Fill(first ? (byte)1 : (byte)2);
            diagnostics.Clear();
            diagnostics.Add(new RenderDiagnostic(first ? "FIRST" : "LATER", first ? "accepted first" : "accepted later"));
            return Task.FromResult(new AssetProducerResult(buffer.AsMemory(1, 3), diagnostics));
        });
        var pipeline = new AssetPipeline(_engine, producer);

        var build = await pipeline.RunAsync(manifest, false);
        var snapshot = Snapshot();
        var check = await pipeline.RunAsync(manifest, true);
        diagnostics.Clear();
        buffer.AsSpan().Fill(42);

        Assert.True(build.Success);
        Assert.True(check.Success);
        Assert.Equal(4, calls);
        Assert.Equal(new byte[] { 1, 1, 1 }, File.ReadAllBytes(Full("generated/first.svg")));
        Assert.Equal(new byte[] { 2, 2, 2 }, File.ReadAllBytes(Full("generated/later.png")));
        var expected = new[]
        {
            new AssetPipelineDiagnostic("inputs/first.json", "generated/first.svg", "FIRST", "accepted first"),
            new AssetPipelineDiagnostic("inputs/later.json", "generated/later.png", "LATER", "accepted later")
        };
        Assert.Equal(expected, build.Diagnostics);
        Assert.Equal(expected, check.Diagnostics);
        Assert.Equal(snapshot, Snapshot());
    }

    [Fact]
    public async Task SpriteComposition_UsesFrozenSlicesWithDistinctSymbolAndDescendantIds()
    {
        Save("first.json", Document("alpha"));
        Save("later.json", Document("bravo"));
        var manifest = Manifest("""
            {"version":1,"outputRoot":"generated","sprites":[
             {"sources":["first.json","later.json"],"output":"generated/sprite.svg"}]}
            """);
        var fragments = new[]
        {
            "<symbol id=\"alpha\"><path id=\"path-a\" d=\"M0 0\"/></symbol>\n",
            "<symbol id=\"bravo\"><path id=\"path-b\" d=\"M1 1\"/></symbol>\n"
        };
        var buffer = Enumerable.Repeat((byte)'!', Encoding.UTF8.GetByteCount(fragments[0]) + 7).ToArray();
        var calls = 0;
        var producer = new TestProducer((request, _, _) =>
        {
            Assert.True(request.AsSymbol);
            var bytes = Encoding.UTF8.GetBytes(fragments[calls++]);
            bytes.CopyTo(buffer, 3);
            return Task.FromResult(new AssetProducerResult(buffer.AsMemory(3, bytes.Length), []));
        });

        var result = await new AssetPipeline(_engine, producer).RunAsync(manifest, false);

        Assert.True(result.Success);
        Assert.Equal(2, calls);
        var text = File.ReadAllText(Full("generated/sprite.svg"));
        Assert.Contains(fragments[0] + fragments[1], text);
        XNamespace svg = "http://www.w3.org/2000/svg";
        var sprite = XDocument.Parse(text);
        Assert.Equal(["alpha", "bravo"], sprite.Descendants(svg + "symbol").Select(node => (string?)node.Attribute("id")));
        Assert.Equal(["path-a", "path-b"], sprite.Descendants(svg + "path").Select(node => (string?)node.Attribute("id")));
    }

    [Theory]
    [InlineData("asset")]
    [InlineData("sprite-fragment")]
    [InlineData("aggregate")]
    public async Task ActualReturnedBytes_AreLimitedEvenWhenProducerChargesNoWork(string kind)
    {
        Save("source.json", Document("icon"));
        var aggregate = kind == "aggregate";
        var manifest = kind == "sprite-fragment"
            ? Manifest("""
                {"version":1,"outputRoot":"generated","sprites":[
                 {"sources":["source.json"],"output":"generated/sprite.svg"}]}
                """)
            : AssetManifest(aggregate ? 3 : 1);
        var data = new byte[aggregate ? AssetPipeline.MaxAssetBytes : AssetPipeline.MaxAssetBytes + 1];
        var calls = 0;
        var producer = new TestProducer((_, _, _) =>
        {
            calls++;
            return Task.FromResult(new AssetProducerResult(data, []));
        });
        var snapshot = Snapshot();

        var error = await Assert.ThrowsAsync<DocumentEngineException>(() =>
            new AssetPipeline(_engine, producer).RunAsync(manifest, false));

        Assert.Equal(EngineErrors.ResourceLimit, error.Code);
        Assert.Equal(aggregate ? 3 : 1, calls);
        Assert.Equal(snapshot, Snapshot());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedResultBytes_AreBoundedBeforeAccessingTheirSpan(bool aggregate)
    {
        Save("source.json", Document("icon"));
        Write("generated/0.svg", "previous");
        var acceptedCount = aggregate ? 2 : 0;
        var manifest = AssetManifest(acceptedCount + 1);
        var accepted = aggregate ? new byte[AssetPipeline.MaxAssetBytes] : [];
        using var rejected = new UnreadableMemory(aggregate ? 1 : AssetPipeline.MaxAssetBytes + 1);
        var calls = 0;
        var producer = new TestProducer((_, _, _) => Task.FromResult(new AssetProducerResult(
            ++calls <= acceptedCount ? accepted : rejected.Data, [])));
        var snapshot = Snapshot();

        var error = await Assert.ThrowsAsync<DocumentEngineException>(() =>
            new AssetPipeline(_engine, producer).RunAsync(manifest, false));

        Assert.Equal(EngineErrors.ResourceLimit, error.Code);
        Assert.Equal(acceptedCount + 1, calls);
        Assert.False(rejected.SpanAccessed);
        Assert.Equal(snapshot, Snapshot());
    }

    [Theory]
    [InlineData(false, EngineErrors.ResourceLimit)]
    [InlineData(true, EngineErrors.InvalidArguments)]
    public async Task OversizedFinalSprite_ValidatesIdsBeforeApplyingTheFinalByteLimit(bool duplicateId, string code)
    {
        Save("source.json", Document("icon"));
        var manifest = Manifest("""
            {"version":1,"outputRoot":"generated","sprites":[
             {"sources":["source.json","source.json"],"output":"generated/sprite.svg"}]}
            """);
        var calls = 0;
        var producer = new TestProducer((_, _, _) =>
        {
            var id = calls++ == 0 || duplicateId ? "alpha" : "bravo";
            var data = new byte[AssetPipeline.MaxAssetBytes / 2];
            data.AsSpan().Fill((byte)' ');
            Encoding.UTF8.GetBytes($"<symbol id=\"{id}\"/>").CopyTo(data, 0);
            return Task.FromResult(new AssetProducerResult(data, []));
        });
        var snapshot = Snapshot();

        var error = await Assert.ThrowsAsync<DocumentEngineException>(() =>
            new AssetPipeline(_engine, producer).RunAsync(manifest, false));

        Assert.Equal(code, error.Code);
        if (duplicateId)
            Assert.Contains("alpha", error.Message);
        Assert.Equal(2, calls);
        Assert.Equal(snapshot, Snapshot());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SpriteAggregateBytes_AddWrapperOverheadWithoutChargingFragmentsTwice(bool exceedsWithWrappers)
    {
        Save("source.json", Document("icon"));
        var manifest = Manifest("""
            {"version":1,"outputRoot":"generated",
             "assets":[{"source":"source.json","output":"generated/icon.svg"}],
             "sprites":[
              {"sources":["source.json"],"output":"generated/first.svg"},
              {"sources":["source.json"],"output":"generated/second.svg"}]}
            """);
        var calls = 0;
        var producer = new TestProducer((request, _, _) =>
        {
            calls++;
            var length = request.AsSymbol
                ? AssetPipeline.MaxAssetBytes / 2 - (exceedsWithWrappers ? 0 : 1024) : AssetPipeline.MaxAssetBytes;
            var data = new byte[length];
            if (request.AsSymbol)
            {
                data.AsSpan().Fill((byte)' ');
                "<symbol id=\"icon\"/>"u8.CopyTo(data);
            }
            return Task.FromResult(new AssetProducerResult(data, []));
        });
        var pipeline = new AssetPipeline(_engine, producer);
        var snapshot = Snapshot();

        if (exceedsWithWrappers)
        {
            var error = await Assert.ThrowsAsync<DocumentEngineException>(() => pipeline.RunAsync(manifest, false));
            Assert.Equal(EngineErrors.ResourceLimit, error.Code);
        }
        else
        {
            var result = await pipeline.RunAsync(manifest, true);
            Assert.True(result.Check);
            Assert.Equal(3, result.Files.Count);
            Assert.All(result.Files, file => Assert.Equal("missing", file.Status));
        }
        Assert.Equal(3, calls);
        Assert.Equal(snapshot, Snapshot());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BudgetRejectsNegativeAndOverflowingCharges_WithoutRefundingOrConsumingCapacity(bool raster)
    {
        Save("source.json", Document("icon"));
        var calls = 0;
        var producer = new TestProducer((_, budget, _) =>
        {
            calls++;
            Action<long> charge = raster ? budget.ChargeRasterPixels : count => budget.ChargeNodes(checked((int)count));
            var limit = raster ? AssetPipeline.MaxRasterPixels : AssetPipeline.MaxNodes;
            charge(1);
            foreach (var invalid in new[] { -1L, raster ? long.MinValue : int.MinValue, raster ? long.MaxValue : int.MaxValue })
                Assert.Equal(EngineErrors.ResourceLimit, Assert.Throws<DocumentEngineException>(() => charge(invalid)).Code);
            charge(limit - 1);
            charge(0);
            Assert.Equal(EngineErrors.ResourceLimit, Assert.Throws<DocumentEngineException>(() => charge(1)).Code);
            return Task.FromResult(new AssetProducerResult(new byte[] { 1 }, []));
        });

        Assert.True((await new AssetPipeline(_engine, producer).RunAsync(AssetManifest(1), false)).Success);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BudgetExhaustion_IsSharedAcrossOccurrences_ButFreshAfterFailureAndSuccess(bool raster)
    {
        Save("source.json", Document("icon"));
        var manifest = AssetManifest(2);
        var calls = 0;
        var producer = new TestProducer((_, budget, _) =>
        {
            calls++;
            if (raster)
                budget.ChargeRasterPixels(AssetPipeline.MaxRasterPixels / 2 + 1);
            else
                budget.ChargeNodes(AssetPipeline.MaxNodes / 2 + 1);
            return Task.FromResult(new AssetProducerResult(new byte[] { 1 }, []));
        });
        var pipeline = new AssetPipeline(_engine, producer);
        var snapshot = Snapshot();

        var error = await Assert.ThrowsAsync<DocumentEngineException>(() => pipeline.RunAsync(manifest, false));

        Assert.Equal(EngineErrors.ResourceLimit, error.Code);
        Assert.Equal(2, calls);
        Assert.Equal(snapshot, Snapshot());
        manifest = AssetManifest(1);
        Assert.True((await pipeline.RunAsync(manifest, false)).Success);
        Assert.True((await pipeline.RunAsync(manifest, true)).Success);
        Assert.Equal(4, calls);
    }

    [Fact]
    public async Task ControlledOverlappingRuns_HaveIndependentBudgetsAndResultStorage()
    {
        Save("first.json", Document("first"));
        Save("second.json", Document("second"));
        var firstManifest = Manifest("""
            {"version":1,"outputRoot":"first-output","assets":[{"source":"first.json","output":"first-output/icon.svg"}]}
            """, "first-manifest.json");
        var secondManifest = Manifest("""
            {"version":1,"outputRoot":"second-output","assets":[{"source":"second.json","output":"second-output/icon.svg"}]}
            """, "second-manifest.json");
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecond = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var producer = new TestProducer(async (request, budget, ct) =>
        {
            var first = request.Document.Name == "first";
            budget.ChargeNodes(AssetPipeline.MaxNodes / 2 + 1);
            budget.ChargeRasterPixels(AssetPipeline.MaxRasterPixels / 2 + 1);
            (first ? firstEntered : secondEntered).SetResult();
            await (first ? releaseFirst : releaseSecond).Task.WaitAsync(ct);
            budget.ChargeNodes(AssetPipeline.MaxNodes / 2 - 1);
            budget.ChargeRasterPixels(AssetPipeline.MaxRasterPixels / 2 - 1);
            return new AssetProducerResult(Encoding.UTF8.GetBytes(request.Document.Name), []);
        });
        var pipeline = new AssetPipeline(_engine, producer);
        var runs = new List<Task<AssetPipelineResult>> { pipeline.RunAsync(firstManifest, false, timeout.Token) };
        try
        {
            // Only the producer awaits overlap; real engine calls and host consumption stay sequential.
            await firstEntered.Task.WaitAsync(timeout.Token);
            runs.Add(pipeline.RunAsync(secondManifest, false, timeout.Token));
            await Task.WhenAny(secondEntered.Task, runs[1]).WaitAsync(timeout.Token);
            if (runs[1].IsCompleted)
                await runs[1];
            releaseSecond.SetResult();
            Assert.True((await runs[1]).Success);
            releaseFirst.SetResult();
            Assert.True((await runs[0]).Success);
        }
        finally
        {
            releaseFirst.TrySetResult();
            releaseSecond.TrySetResult();
            await Task.WhenAll(runs);
        }

        Assert.Equal("first", File.ReadAllText(Full("first-output/icon.svg")));
        Assert.Equal("second", File.ReadAllText(Full("second-output/icon.svg")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateHandledProducerFailureOrCancellation_DoesNotPublishAcceptedEarlierResults(bool cancel)
    {
        Save("source.json", Document("icon"));
        Write("generated/0.svg", "previous");
        var manifest = AssetManifest(2);
        using var cancellation = new CancellationTokenSource();
        var failure = new DocumentEngineException("PRODUCER_FAILED", "handled producer failure");
        var calls = 0;
        var producer = new TestProducer((_, _, ct) =>
        {
            Assert.Equal(cancellation.Token, ct);
            if (++calls == 2)
            {
                if (!cancel)
                    throw failure;
                cancellation.Cancel();
            }
            return Task.FromResult(new AssetProducerResult("new bytes"u8.ToArray(), []));
        });
        var pipeline = new AssetPipeline(_engine, producer);
        var snapshot = Snapshot();

        if (cancel)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pipeline.RunAsync(manifest, false, cancellation.Token));
        else
            Assert.Same(failure, await Assert.ThrowsAsync<DocumentEngineException>(() =>
                pipeline.RunAsync(manifest, false, cancellation.Token)));
        Assert.Equal(2, calls);
        Assert.Equal(snapshot, Snapshot());
    }

    [Fact]
    public async Task CancellationBeforeYieldingDiagnostic_PreservesTokenAndStopsEnumerationAndProduction()
    {
        Save("source.json", Document("icon"));
        Write("generated/0.svg", "previous");
        var manifest = AssetManifest(2);
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        var producer = new TestProducer((_, _, ct) =>
        {
            Assert.Equal(cancellation.Token, ct);
            calls++;
            return Task.FromResult(new AssetProducerResult("new bytes"u8.ToArray(),
                new DeferredDiagnostics(Diagnostics, 2)));
        });
        var snapshot = Snapshot();

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new AssetPipeline(_engine, producer).RunAsync(manifest, false, cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(1, calls);
        Assert.Equal(snapshot, Snapshot());

        IEnumerable<RenderDiagnostic> Diagnostics()
        {
            cancellation.Cancel();
            yield return new RenderDiagnostic("VALID", "Cancellation must be checked before accepting this diagnostic");
            throw new InvalidOperationException("The host must not advance after cancellation");
        }
    }

    [Theory]
    [InlineData("result")]
    [InlineData("diagnostics")]
    [InlineData("diagnostic")]
    [InlineData("enumeration")]
    public async Task InvalidResultsAndLazyDiagnosticFailures_AreSourceQualifiedBeforeBytesAndPublication(string kind)
    {
        Save("source.json", Document("icon"));
        Write("generated/0.svg", "previous");
        var manifest = AssetManifest(2);
        var calls = 0;
        var producer = new TestProducer((_, _, _) =>
        {
            if (++calls == 1)
                return Task.FromResult(new AssetProducerResult(new byte[] { 1 }, []));
            if (kind == "result")
                return Task.FromResult<AssetProducerResult>(null!);
            IReadOnlyList<RenderDiagnostic> diagnostics = kind switch
            {
                "diagnostics" => null!,
                "diagnostic" => new RenderDiagnostic[] { null! },
                _ => new DeferredDiagnostics(FailDuringEnumeration, 2)
            };
            return Task.FromResult(new AssetProducerResult(new byte[AssetPipeline.MaxAssetBytes + 1], diagnostics));
        });
        var snapshot = Snapshot();

        var error = await Assert.ThrowsAsync<DocumentEngineException>(() =>
            new AssetPipeline(_engine, producer).RunAsync(manifest, false));

        Assert.Equal("ASSET_EXPORT_FAILED", error.Code);
        Assert.Contains(Full("source.json"), error.Message);
        Assert.Equal(2, calls);
        Assert.Equal(snapshot, Snapshot());

        static IEnumerable<RenderDiagnostic> FailDuringEnumeration()
        {
            yield return new RenderDiagnostic("ACCEPTED", "before failure");
            throw new InvalidOperationException("lazy diagnostic failure");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaturatedDiagnostics_SkipOmittedFieldsAndEnumeration_AndKeepReplacedSlotIdentity(bool nullDiagnostic)
    {
        Save("inputs/first.json", Document("first"));
        Save("inputs/later.json", Document("later"));
        var manifest = Manifest("""
            {"version":1,"outputRoot":"generated","assets":[
             {"source":"inputs/first.json","output":"generated/first.svg"},
             {"source":"inputs/later.json","output":"generated/later.svg"}]}
            """);
        var code = new string('c', 128);
        var diagnostics = Enumerable.Repeat(new RenderDiagnostic(code,
            new string('m', AssetPipeline.MaxDiagnosticMessageCharacters + 1)), AssetPipeline.MaxDiagnostics).ToArray();
        var calls = 0;
        var producer = new TestProducer((_, _, _) => Task.FromResult(new AssetProducerResult(new byte[] { 1 },
            calls++ == 0 ? diagnostics : new DeferredDiagnostics(Omitted, 2))));

        var result = await new AssetPipeline(_engine, producer).RunAsync(manifest, false);

        Assert.True(result.Success);
        Assert.Equal(2, calls);
        Assert.Equal(AssetPipeline.MaxDiagnostics, result.Diagnostics.Count);
        Assert.All(result.Diagnostics, diagnostic =>
        {
            Assert.Equal("inputs/first.json", diagnostic.Source);
            Assert.Equal("generated/first.svg", diagnostic.Output);
        });
        Assert.All(result.Diagnostics.Take(AssetPipeline.MaxDiagnostics - 1), diagnostic =>
        {
            Assert.Equal(code, diagnostic.Code);
            Assert.Equal(AssetPipeline.MaxDiagnosticMessageCharacters, diagnostic.Message.Length);
            Assert.EndsWith("... [truncated]", diagnostic.Message);
        });
        Assert.Equal(RenderDiagnosticCodes.DiagnosticsTruncated, result.Diagnostics[^1].Code);

        IEnumerable<RenderDiagnostic> Omitted()
        {
            yield return nullDiagnostic ? null! : new RenderDiagnostic(new string('c', 129), null!);
            throw new InvalidOperationException("The host must stop enumerating after saturation");
        }
    }

    [Fact]
    public async Task OverlongDiagnosticCode_PrecedesInvalidMessageAndLaterEnumerationFailure()
    {
        Save("source.json", Document("icon"));
        var producer = new TestProducer((_, _, _) => Task.FromResult(new AssetProducerResult(new byte[] { 1 },
            new DeferredDiagnostics(Diagnostics, 2))));

        var error = await Assert.ThrowsAsync<DocumentEngineException>(() =>
            new AssetPipeline(_engine, producer).RunAsync(AssetManifest(1), false));

        Assert.Equal(EngineErrors.ResourceLimit, error.Code);
        Assert.False(Directory.Exists(Full("generated")));

        static IEnumerable<RenderDiagnostic> Diagnostics()
        {
            yield return new RenderDiagnostic(new string('c', 129), null!);
            throw new InvalidOperationException("Code limit must be checked before advancing");
        }
    }

    private Document Document(string name) => _engine.CreateDocument(name, 24, 24);

    private string Full(string relative) => Path.Combine(_directory, relative);
    private void Save(string relative, Document document) => Write(relative, _engine.ToJson(document));
    private void Write(string relative, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Full(relative))!);
        File.WriteAllText(Full(relative), text);
    }

    private string Manifest(string json, string relative = "manifest.json")
    {
        Write(relative, json);
        return Full(relative);
    }

    private string AssetManifest(int count) => Manifest(JsonSerializer.Serialize(new
    {
        version = 1,
        outputRoot = "generated",
        assets = Enumerable.Range(0, count).Select(index => new { source = "source.json", output = $"generated/{index}.svg" })
    }));

    private string[] Snapshot() => Directory.GetFileSystemEntries(_directory, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal)
        .Select(path => Directory.Exists(path) ? $"directory:{path}:{Directory.GetLastWriteTimeUtc(path).Ticks}"
            : $"file:{path}:{File.GetLastWriteTimeUtc(path).Ticks}:{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))}")
        .ToArray();

    private sealed class TestProducer(
        Func<VectorAssetRequest, IAssetProductionBudget, CancellationToken, Task<AssetProducerResult>> produce)
        : IAssetProducer<VectorAssetRequest>
    {
        public Task<AssetProducerResult> ProduceAsync(VectorAssetRequest request, IAssetProductionBudget budget,
            CancellationToken cancellationToken = default) => produce(request, budget, cancellationToken);
    }

    private sealed class UnreadableMemory(int length) : MemoryManager<byte>
    {
        public bool SpanAccessed { get; private set; }
        // Avoid the base Memory getter, which calls GetSpan while constructing the view.
        public ReadOnlyMemory<byte> Data => CreateMemory(length);
        public override Span<byte> GetSpan()
        {
            SpanAccessed = true;
            throw new InvalidOperationException("Rejected memory must not be read");
        }
        public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();
        public override void Unpin() { }
        protected override void Dispose(bool disposing) { }
    }

    private sealed class DeferredDiagnostics(Func<IEnumerable<RenderDiagnostic>> enumerate, int count)
        : IReadOnlyList<RenderDiagnostic>
    {
        public int Count => count;
        public RenderDiagnostic this[int index] => throw new NotSupportedException("Enumeration only");
        public IEnumerator<RenderDiagnostic> GetEnumerator() => enumerate().GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
