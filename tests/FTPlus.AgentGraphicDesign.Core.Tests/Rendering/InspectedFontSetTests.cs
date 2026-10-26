using FTPlus.AgentGraphicDesign.Core.Platform;
using FTPlus.AgentGraphicDesign.Core.Rendering;
using FTPlus.AgentGraphicDesign.Core.Serialization;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Rendering;

public sealed class InspectedFontSetTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "agd-font-input-" + Guid.NewGuid().ToString("N"));
    public InspectedFontSetTests() => Directory.CreateDirectory(_root);
    private static bool Admitted => PlatformProfile.Current.GateAdmits(PlatformCapability.AssetPipeline);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HandwrittenOrderLengthsAndSkips(bool inspect)
    {
        if (inspect && !Admitted) return;
        var directory = Directory.CreateDirectory(Path.Combine(_root, "fonts")).FullName;
        Directory.CreateDirectory(Path.Combine(_root, "empty"));
        File.WriteAllText(Path.Combine(directory, "a.otf"), "123");
        File.WriteAllText(Path.Combine(directory, "z.txt"), "not a font");
        File.WriteAllText(Path.Combine(directory, "a.TTF"), "12");
        File.WriteAllText(Path.Combine(directory, "B.ttf"), "1");
        using (var file = File.Create(Path.Combine(directory, "big.ttf"))) file.SetLength(8 * 1024 * 1024 + 1);
        var set = InspectedFontSet.Inspect(["fonts", "empty"], _root, inspect, default);
        Assert.Equal(new[] { directory, Path.Combine(_root, "empty") }, set.Directories);
        Assert.Equal(new[] { "B.ttf", "a.TTF", "a.otf", "big.ttf" }, set.Entries.Select(entry => Path.GetFileName(entry.Path)));
        Assert.Equal(new long[] { 1, 2, 3 }, set.Entries.OfType<FontEntry.Load>().Select(entry => entry.Length));
        Assert.Equal(FontSkipReason.Oversize, Assert.IsType<FontEntry.Skip>(set.Entries[3]).Reason);
        Assert.Equal(6, set.CountedBytes);
        Assert.Empty(typeof(InspectedFontSet).GetConstructors());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EntryCountBoundIsExact(bool inspect)
    {
        if (inspect && !Admitted) return;
        for (var i = 0; i < 256; i++) File.WriteAllText(Path.Combine(_root, $"{i:D3}.ttf"), "x");
        Assert.Equal(256, InspectedFontSet.Inspect([_root], _root, inspect, default).Entries.Length);
        File.WriteAllText(Path.Combine(_root, inspect ? "other.txt" : "other.ttf"), "x");
        Assert.Equal(EngineErrors.ResourceLimit, Assert.Throws<DocumentEngineException>(() => InspectedFontSet.Inspect([_root], _root, inspect, default)).Code);
    }

    [Fact]
    public void NonAdmittedRulesSkipLinksBeforeLengthAndRefuseLinkedDirectories()
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, "fonts")).FullName;
        File.WriteAllText(Path.Combine(_root, "outside-fonts.ttf"), "123");
        File.WriteAllText(Path.Combine(directory, "real.ttf"), "12");
        File.CreateSymbolicLink(Path.Combine(directory, "inside.ttf"), Path.Combine(directory, "real.ttf"));
        File.CreateSymbolicLink(Path.Combine(directory, "outside.ttf"), Path.Combine(_root, "outside-fonts.ttf"));
        File.CreateSymbolicLink(Path.Combine(directory, "broken.ttf"), Path.Combine(_root, "absent"));
        var set = InspectedFontSet.Inspect([directory], _root, false, default);
        Assert.Equal(new[] { "broken.ttf", "inside.ttf", "outside.ttf", "real.ttf" }, set.Entries.Select(entry => Path.GetFileName(entry.Path)));
        Assert.All(set.Entries.Take(3), entry => Assert.Equal(FontSkipReason.SymbolicLink, Assert.IsType<FontEntry.Skip>(entry).Reason));
        Assert.Equal(2, set.CountedBytes);
        Directory.CreateSymbolicLink(Path.Combine(_root, "linked"), directory);
        Assert.Throws<ArgumentException>(() => InspectedFontSet.Inspect(["linked"], _root, false, default));
    }

    [Fact]
    public void InspectionRefusesEvenNonFontLinksAndDoesNotRecurse()
    {
        if (!Admitted) return;
        var child = Directory.CreateDirectory(Path.Combine(_root, "child")).FullName;
        File.CreateSymbolicLink(Path.Combine(child, "hidden.ttf"), Path.Combine(_root, "absent"));
        Assert.Empty(InspectedFontSet.Inspect([_root], _root, true, default).Entries);
        File.CreateSymbolicLink(Path.Combine(_root, "link.txt"), Path.Combine(_root, "absent"));
        Assert.Equal(EngineErrors.PathEscape, Assert.Throws<DocumentEngineException>(() => InspectedFontSet.Inspect([_root], _root, true, default)).Code);
    }

    [Fact]
    public async Task NonAdmittedFontsStillCountAtAggregateBoundaryAndOversizeDoesNot()
    {
        var json = DocumentSerializer.Serialize(new Documents.Document { Name = "fonts" });
        File.WriteAllText(Path.Combine(_root, "doc.json"), json);
        var fonts = Directory.CreateDirectory(Path.Combine(_root, "fonts")).FullName;
        File.WriteAllText(Path.Combine(fonts, "a.ttf"), "1234");
        using (var file = File.Create(Path.Combine(fonts, "big.ttf"))) file.SetLength(8 * 1024 * 1024 + 1);
        var expected = new FileInfo(Path.Combine(_root, "doc.json")).Length + 4;
        var opens = 0;
        Stream Open(string path) { opens++; return File.OpenRead(path); }
        var result = await new ReferenceReader(false, expected, Open).ReadAsync([new("doc.json")], [fonts], _root);
        Assert.Equal(expected, result.CountedBytes);
        Assert.Equal(1, opens);
        opens = 0;
        var error = await Assert.ThrowsAsync<DocumentEngineException>(() => new ReferenceReader(false, expected - 1, Open).ReadAsync([new("doc.json")], [fonts], _root));
        Assert.Equal("INPUT_TOO_LARGE", error.Code);
        Assert.Equal(0, opens);
    }

    [Fact]
    public void DirectoryCountAndCancellationAreBoundedBeforeEnumeration()
    {
        Assert.Equal(16, InspectedFontSet.Inspect(Enumerable.Repeat(_root, 16).ToArray(), _root, false, default).Directories.Length);
        Assert.Equal(EngineErrors.ResourceLimit, Assert.Throws<DocumentEngineException>(() => InspectedFontSet.Inspect(Enumerable.Repeat(_root, 17).ToArray(), _root, false, default)).Code);
        Assert.ThrowsAny<OperationCanceledException>(() => InspectedFontSet.Inspect([_root], _root, false, new CancellationToken(true)));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
