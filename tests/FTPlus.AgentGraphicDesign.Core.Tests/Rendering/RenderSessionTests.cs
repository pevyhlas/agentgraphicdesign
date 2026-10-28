using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Rendering;
using FTPlus.AgentGraphicDesign.Skia;
using FTPlus.AgentGraphicDesign.Skia.TextShaping;
using SkiaSharp;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Rendering;

public sealed class RenderSessionTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("agd-session-").FullName;
    private static readonly RenderOptions Options = new() { SystemFonts = false, Format = RenderFormat.Rgba8 };

    private string Font(string name = "a.ttf")
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, SessionTestFont.Create());
        return path;
    }

    private InspectedFontSet Inspect() => InspectedFontSet.Inspect([_root], _root, false, default);
    private InspectedFontSet Empty() => InspectedFontSet.Inspect([], _root, false, default);

    [Fact]
    public void ManySequentialRendersLoadOnlyTheInspectedPathsOnce()
    {
        Font("z.ttf"); Font("A.ttf");
        var set = Inspect();
        Font("late.ttf");
        var loaded = new List<string>();
        var faces = new List<SKTypeface>();
        using (var session = new SkiaRenderSession(set, default, path =>
        {
            loaded.Add(path);
            var face = SKTypeface.FromFile(path);
            Assert.NotNull(face);
            faces.Add(face);
            return face;
        }))
        {
            for (var i = 0; i < 32; i++)
            {
                var result = session.Render(Scene(SessionTestFont.Family), Options with { StrictFonts = true });
                Assert.NotEmpty(result.Data);
                Assert.Empty(result.Diagnostics);
                Assert.All(faces, face => Assert.NotEqual(IntPtr.Zero, face.Handle));
            }
            Assert.Equal(new[] { Path.Combine(_root, "A.ttf"), Path.Combine(_root, "z.ttf") }
                .Select(path => FTPlus.AgentGraphicDesign.Core.Paths.PlatformPaths.FullPath(path)), loaded);
        }
        Assert.All(faces, face => Assert.Equal(IntPtr.Zero, face.Handle));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PartialLoadDisposesEveryNativeFaceOnCancellationOrException(bool fail)
    {
        Font("a.ttf"); Font("b.ttf");
        var faces = new List<SKTypeface>();
        using var cancelled = new CancellationTokenSource();
        var calls = 0;
        SKTypeface? Load(string path)
        {
            calls++;
            if (fail && calls == 2) throw new IOException("load seam failure");
            var face = SKTypeface.FromFile(path)!;
            faces.Add(face);
            if (!fail) cancelled.Cancel();
            return face;
        }
        if (fail)
            Assert.Throws<IOException>(() => new SkiaRenderSession(Inspect(), cancelled.Token, Load));
        else
            Assert.ThrowsAny<OperationCanceledException>(() => new SkiaRenderSession(Inspect(), cancelled.Token, Load));
        Assert.Equal(fail ? 2 : 1, calls);
        Assert.Single(faces);
        Assert.All(faces, face => Assert.Equal(IntPtr.Zero, face.Handle));
    }

    [Fact]
    public void CancellationOnTheLastLoadAlsoDisposesItsFace()
    {
        Font();
        SKTypeface? loaded = null;
        using var cancelled = new CancellationTokenSource();
        Assert.ThrowsAny<OperationCanceledException>(() => new SkiaRenderSession(Inspect(), cancelled.Token, path =>
        {
            loaded = SKTypeface.FromFile(path);
            cancelled.Cancel();
            return loaded;
        }));
        Assert.NotNull(loaded);
        Assert.Equal(IntPtr.Zero, loaded.Handle);
    }

    [Fact]
    public void SessionCallerDisposesOnRenderCancellationAndFailure()
    {
        Font();
        foreach (var cancel in new[] { false, true })
        {
            SKTypeface? face = null;
            var session = new SkiaRenderSession(Inspect(), default, path => face = SKTypeface.FromFile(path));
            using (session)
            {
                if (cancel)
                    Assert.ThrowsAny<OperationCanceledException>(() => session.Render(Scene(), Options, new CancellationToken(true)));
                else
                    Assert.Throws<ArgumentException>(() => session.Render(Scene(), Options with { Scale = 0 }));
            }
            Assert.NotNull(face);
            Assert.Equal(IntPtr.Zero, face.Handle);
            session.Dispose();
            Assert.Throws<ObjectDisposedException>(() => session.Render(Scene(), Options));
        }
    }

    [Fact]
    public void DefaultSessionRejectsEvenSkipOnlySetsAndEnforcesItsLifetime()
    {
        var renderer = new ForwardingRenderer();
        using var session = ((IRenderer)renderer).OpenSession(Empty());
        var doc = Scene();
        session.Render(doc, Options);
        Assert.Same(doc, renderer.LastDocument);
        Assert.Throws<ArgumentException>(() => session.Render(doc, Options with { FontDirectories = [_root] }));
        Assert.ThrowsAny<OperationCanceledException>(() => session.Render(doc, Options, new CancellationToken(true)));
        Assert.Equal(1, renderer.Calls);
        session.Dispose();
        Assert.Throws<ObjectDisposedException>(() => session.Render(doc, Options));
        using (var file = File.Create(Path.Combine(_root, "huge.ttf"))) file.SetLength(8 * 1024 * 1024 + 1);
        Assert.Throws<NotSupportedException>(() => ((IRenderer)renderer).OpenSession(Inspect()));
        Assert.ThrowsAny<OperationCanceledException>(() => ((IRenderer)renderer).OpenSession(Empty(), new CancellationToken(true)));
    }

    [Fact]
    public void SkiaOverridesOpenAndKeepsPolicyAndDiagnosticsPerRender()
    {
        Font();
        IRenderer renderer = new SkiaRenderer();
        using var session = renderer.OpenSession(Inspect());
        Assert.IsType<SkiaRenderSession>(session);
        Assert.Throws<ArgumentException>(() => session.Render(Scene(), Options with { FontDirectories = [_root] }));
        Assert.ThrowsAny<OperationCanceledException>(() => renderer.OpenSession(Inspect(), new CancellationToken(true)));
        var missing = Scene("MissingSessionFont9Z");
        Assert.Single(session.Render(missing, Options).Diagnostics, d => d.Code == RenderDiagnosticCodes.FontSubstituted);
        Assert.Equal(EngineErrors.FontNotFound, Assert.Throws<DocumentEngineException>(() => session.Render(missing, Options with { StrictFonts = true })).Code);
        Assert.Empty(session.Render(Scene(SessionTestFont.Family), Options with { StrictFonts = true }).Diagnostics);
        Assert.Single(session.Render(missing, Options).Diagnostics, d => d.Code == RenderDiagnosticCodes.FontSubstituted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectoryFontsMatchOrdinaryRenderingAtThreeSizesAndWithARegionMask(bool inter)
    {
        if (inter)
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "FTPlus.AgentGraphicDesign.slnx"))) root = root.Parent;
            Assert.NotNull(root);
            File.Copy(Path.Combine(root.FullName, "src", "FTPlus.AgentGraphicDesign.Skia", "Fonts", "Inter-Bold.ttf"), Path.Combine(_root, "bold.ttf"));
        }
        else Font();
        File.WriteAllText(Path.Combine(_root, "broken.ttf"), "invalid font");
        var family = inter ? "Inter" : SessionTestFont.Family;
        IRenderer renderer = new SkiaRenderer();
        using var session = renderer.OpenSession(Inspect());
        using var empty = renderer.OpenSession(Empty());
        var source = Scene(family);
        var text = source.Root.Children[0];
        source.Root.Children.Add(new RectangleNode { Width = 12, Height = 12, Order = "a1" });
        // Canvas cropping ignores measured bounds; the builder still asks for diagnostics.
        var builder = new TransientDocumentBuilder((_, _, _, _) => new TransientMeasurement(null, []));
        var transient = builder.Build(source);
        foreach (var region in new[] { false, true })
        {
            transient.ApplyMask(region ? [text.Id] : null);
            foreach (var scale in new[] { 0.5, 1, 2 })
            {
                var options = Options with { Scale = scale };
                var expected = renderer.Render(transient.Document, options with { FontDirectories = [_root] });
                var actual = session.Render(transient.Document, options);
                Assert.Equal(expected.Data, actual.Data);
                Assert.Equal(expected.Diagnostics, actual.Diagnostics);
                Assert.Equal((expected.Width, expected.Height), (actual.Width, actual.Height));
                Assert.False(empty.Render(transient.Document, options).Data.SequenceEqual(actual.Data), "font anchor must change actual pixels");
                Assert.Contains(actual.Data.Where((_, i) => i % 4 == 3), alpha => alpha != 0);
                Assert.DoesNotContain(actual.Diagnostics, d => d.Code == RenderDiagnosticCodes.FontSubstituted);
            }
        }
    }

    [Fact]
    public void SkipsAndUnreadableDiagnosticsRemainOrderedCappedAndNeverOpened()
    {
        Font("a.ttf");
        File.CreateSymbolicLink(Path.Combine(_root, "b.ttf"), Path.Combine(_root, "a.ttf"));
        using (var file = File.Create(Path.Combine(_root, "c.ttf"))) file.SetLength(8 * 1024 * 1024 + 1);
        File.WriteAllText(Path.Combine(_root, "d.ttf"), "invalid");
        for (var i = 0; i < 34; i++) File.WriteAllText(Path.Combine(_root, $"z{i:D2}.ttf"), "invalid");
        var set = Inspect();
        Assert.Equal(new FileInfo(Path.Combine(_root, "a.ttf")).Length + 35 * 7, set.CountedBytes);
        var opened = new List<string>();
        using var catalog = FontDirectoryCatalog.FromInspected(set, default, path => { opened.Add(path); return SKTypeface.FromFile(path); });
        Assert.Equal(set.Entries.OfType<FontEntry.Load>().Select(e => e.Path), opened);
        Assert.Equal(33, catalog.Diagnostics.Count);
        Assert.Contains("'b.ttf'", catalog.Diagnostics[0].Message);
        Assert.Contains("symbolic link", catalog.Diagnostics[0].Message);
        Assert.Contains("'c.ttf'", catalog.Diagnostics[1].Message);
        Assert.Contains("8388608", catalog.Diagnostics[1].Message);
        Assert.Contains("'d.ttf'", catalog.Diagnostics[2].Message);
        Assert.Equal(RenderDiagnosticCodes.DiagnosticsTruncated, catalog.Diagnostics[^1].Code);
        using var ordinary = FontDirectoryCatalog.Load([_root]);
        Assert.Equal(ordinary.Diagnostics, catalog.Diagnostics);
    }

    [Fact]
    public void ALoadEntryReplacedByALinkIsNotOpened()
    {
        var original = Font();
        var set = Inspect();
        File.Delete(original);
        File.CreateSymbolicLink(original, Font("late.ttf"));
        using var catalog = FontDirectoryCatalog.FromInspected(set, default, _ => throw new InvalidOperationException("must not open"));
        Assert.Equal(0, catalog.FaceCount);
        Assert.Contains("symbolic link", Assert.Single(catalog.Diagnostics).Message);
    }

    [InspectionFact]
    public void AdmittedSetRendersWithoutReenumeratingAfterItsPrepass()
    {
        Font();
        var set = InspectedFontSet.Inspect([_root], _root, true, default);
        File.CreateSymbolicLink(Path.Combine(_root, "late.txt"), "missing");
        using var session = ((IRenderer)new SkiaRenderer()).OpenSession(set);
        Assert.Empty(session.Render(Scene(SessionTestFont.Family), Options with { StrictFonts = true }).Diagnostics);
    }

    [Fact]
    public void SystemFontPolicyMatchesOrdinaryRenderingBetweenSessionRenders()
    {
        // Some native font managers enumerate no usable system family. That host exercises
        // fallback parity; actual system resolution is asserted only when a family is available.
        var systemFamily = SKFontManager.Default.FontFamilies.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)
            && !name.Equals("Inter", StringComparison.OrdinalIgnoreCase) && TextShapingService.IsFontFamilyAvailable(name));
        IRenderer renderer = new SkiaRenderer();
        using var session = renderer.OpenSession(Empty());
        var scene = Scene(systemFamily ?? "MissingSessionFont9Z");
        foreach (var systemFonts in new[] { false, true, false })
        {
            var options = Options with { SystemFonts = systemFonts };
            var actual = session.Render(scene, options);
            var expected = renderer.Render(scene, options);
            Assert.Equal(expected.Data, actual.Data);
            Assert.Equal(expected.Diagnostics, actual.Diagnostics);
            Assert.Equal(systemFamily is null || !systemFonts,
                actual.Diagnostics.Any(d => d.Code == RenderDiagnosticCodes.FontSubstituted));
        }
        Assert.Equal(EngineErrors.FontNotFound, Assert.Throws<DocumentEngineException>(() => session.Render(scene, Options with { StrictFonts = true })).Code);
    }

    private static Document Scene(string family = "Inter") => new()
    {
        Id = Guid.NewGuid(),
        Canvas = new Canvas { Width = 160, Height = 80 },
        Root = new FrameNode
        {
            Width = 160,
            Height = 80,
            ClipContent = false,
            Children = [new TextNode { Text = "AAA", FontFamily = family, FontWeight = 400, FontSize = 40 }]
        }
    };

    private sealed class ForwardingRenderer : IRenderer
    {
        internal int Calls { get; private set; }
        internal Document? LastDocument { get; private set; }
        public RenderResult Render(Document document, RenderOptions options)
        {
            Calls++; LastDocument = document;
            return new RenderResult { Data = [], MimeType = "test" };
        }
    }

    public void Dispose() => Directory.Delete(_root, true);
}
