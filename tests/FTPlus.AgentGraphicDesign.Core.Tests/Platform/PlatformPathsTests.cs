using FTPlus.AgentGraphicDesign.Core.Paths;
using Xunit;
using static FTPlus.AgentGraphicDesign.Core.Paths.PlatformPaths;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Platform;

public sealed class PlatformPathsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "agd-paths-" + Guid.NewGuid().ToString("N"));

    public PlatformPathsTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static AliasMetadata? ValidMetadata(string path) => path.StartsWith("/private/", StringComparison.Ordinal)
        ? new(0x41ED, 0, null) : new(0xA1FF, 0, "private" + path);

    [Theory]
    [InlineData("/var", "/private/var")]
    [InlineData("/tmp/a", "/private/tmp/a")]
    [InlineData("/etc/a", "/private/etc/a")]
    [InlineData("/tmp/../probe", "/probe")]
    [InlineData("/var/../probe", "/probe")]
    [InlineData("/etc/../probe", "/probe")]
    [InlineData("/./tmp/probe", "/private/tmp/probe")]
    [InlineData("/./var/probe", "/private/var/probe")]
    [InlineData("/./etc/probe", "/private/etc/probe")]
    [InlineData("/tmp/a/../b", "/private/tmp/b")]
    [InlineData("/tmpx/probe", "/tmpx/probe")]
    [InlineData("/VAR/probe", "/VAR/probe")]
    [InlineData("/home/probe", "/home/probe")]
    [InlineData("/private/tmp/probe", "/private/tmp/probe")]
    public void CanonicalIdentity_IsSharedByBothOverloads(string input, string expected)
    {
        var inspected = new List<string>();
        var prefixes = new List<string>();
        var resolver = new CanonicalPathResolver(true, ValidMetadata, (path, _) => prefixes.Add(path));
        Assert.Equal(expected, resolver.FullPath(input));
        Assert.Equal(expected, resolver.FullPath(input, null, inspected.Add));
        Assert.Equal(new[] { expected }, inspected);
        Assert.Equal(expected, resolver.FullPath(expected));
        if (input.EndsWith("/../probe", StringComparison.Ordinal))
        {
            Assert.Equal(new[] { "/private/" + input.Split('/')[1], "/", "/probe" }, prefixes);
            Assert.DoesNotContain("/private/probe", prefixes);
        }
    }

    [Theory]
    [InlineData("/tmp/base", "../file", "/private/tmp/file")]
    [InlineData("/var/base", "../../file", "/file")]
    [InlineData("/etc/base", "../../file", "/file")]
    [InlineData("/", "./tmp/file", "/private/tmp/file")]
    public void RelativeInputs_UseTheOriginalBase(string basis, string input, string expected)
    {
        string? inspected = null;
        var resolver = new CanonicalPathResolver(true, ValidMetadata, (_, _) => { });
        Assert.Equal(expected, resolver.FullPath(input, basis));
        Assert.Equal(expected, resolver.FullPath(input, basis, path => inspected = path));
        Assert.Equal(expected, inspected);
    }

    [Theory]
    [InlineData("link-missing")]
    [InlineData("link-owner")]
    [InlineData("link-mode")]
    [InlineData("absolute-target")]
    [InlineData("wrong-target")]
    [InlineData("wrong-case")]
    [InlineData("target-missing")]
    [InlineData("target-owner")]
    [InlineData("target-link")]
    [InlineData("io")]
    [InlineData("denied")]
    [InlineData("dll")]
    [InlineData("entry")]
    [InlineData("image")]
    [InlineData("unsupported")]
    public void AliasFailure_IsDeniedAndCached(string failure)
    {
        var calls = new List<string>();
        AliasMetadata? Probe(string path)
        {
            calls.Add(path);
            var target = path.StartsWith("/private/", StringComparison.Ordinal);
            var valid = ValidMetadata(path)!.Value;
            return failure switch
            {
                "link-missing" when !target => null,
                "link-owner" when !target => valid with { Uid = 501 },
                "link-mode" when !target => valid with { Mode = 0x41ED },
                "absolute-target" when !target => valid with { LinkTarget = "/private" + path },
                "wrong-target" when !target => valid with { LinkTarget = "private/elsewhere" },
                "wrong-case" when !target => valid with { LinkTarget = "PRIVATE" + path },
                "target-missing" when target => null,
                "target-owner" when target => valid with { Uid = 501 },
                "target-link" when target => valid with { Mode = 0xA1FF },
                "io" => throw new IOException(),
                "denied" => throw new UnauthorizedAccessException(),
                "dll" => throw new DllNotFoundException(),
                "entry" => throw new EntryPointNotFoundException(),
                "image" => throw new BadImageFormatException(),
                "unsupported" => throw new NotSupportedException(),
                _ => valid
            };
        }
        var resolver = new CanonicalPathResolver(true, Probe);
        foreach (var alias in new[] { "/tmp", "/var", "/etc" })
            Assert.Equal(alias + "/file", resolver.FullPath(alias + "/file"));
        var initialCalls = calls.ToArray();
        foreach (var alias in new[] { "/tmp", "/var", "/etc" })
            Assert.Equal(alias + "/again", resolver.FullPath(alias + "/again"));
        Assert.Equal(initialCalls, calls);
        Assert.All(calls, path => Assert.Contains(path, new[] { "/tmp", "/var", "/etc", "/private/tmp", "/private/var", "/private/etc" }));
    }

    [Fact]
    public async Task SuccessfulVerification_IsCachedUnderConcurrentUse()
    {
        var calls = 0;
        var resolver = new CanonicalPathResolver(true, path =>
        {
            Interlocked.Increment(ref calls);
            return ValidMetadata(path);
        });
        var paths = await Task.WhenAll(Enumerable.Range(0, 64).Select(_ => Task.Run(() => resolver.FullPath("/tmp/file"))));
        Assert.All(paths, path => Assert.Equal("/private/tmp/file", path));
        Assert.Equal(6, calls);
    }

    [Fact]
    public void NonMac_DoesNotProbeAliases()
    {
        var resolver = new CanonicalPathResolver(false, _ => throw new InvalidOperationException("Unexpected probe"));
        Assert.Equal("/tmp/file", resolver.FullPath("/tmp/file"));
        Assert.Equal("/file", resolver.FullPath("/tmp/../file"));
    }

    [Fact]
    public void InspectedPath_IsThePathOpened()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        File.WriteAllText(Path.Combine(_root, "document.json"), "sentinel");
        var input = Path.Combine(_root, "sub", "..", "document.json");
        string? inspected = null;
        var resolved = FullPath(input, null, path =>
        {
            Assert.True(File.Exists(path));
            inspected = path;
        });
        Assert.Equal(FullPath(input), resolved);
        Assert.Equal(inspected, resolved);
        Assert.Equal("sentinel", File.ReadAllText(resolved));
        Assert.Equal("document.json", RelativePath(Path.Combine(_root, "sub", ".."), resolved));
    }

    [Theory]
    [InlineData("link", EngineErrors.PathEscape)]
    [InlineData("dangling", EngineErrors.PathEscape)]
    [InlineData("file", EngineErrors.PathEscape)]
    [InlineData("missing", EngineErrors.DocumentNotFound)]
    public void LexicalTraversalCannotDisappearBeforeInspection(string kind, string code)
    {
        var component = Path.Combine(_root, "component");
        Directory.CreateDirectory(Path.Combine(_root, "real"));
        File.WriteAllText(Path.Combine(_root, "document.json"), "not opened");
        if (kind == "link") Directory.CreateSymbolicLink(component, Path.Combine(_root, "real"));
        if (kind == "dangling") Directory.CreateSymbolicLink(component, Path.Combine(_root, "absent"));
        if (kind == "file") File.WriteAllText(component, "not a directory");
        var inspected = false;
        var error = Assert.Throws<DocumentEngineException>(() =>
            FullPath(Path.Combine(component, "..", "document.json"), null, _ => inspected = true));
        Assert.Equal(code, error.Code);
        Assert.False(inspected);
    }

    [Fact]
    public void FailedFinalInspection_NeverReturnsAPath()
    {
        var file = Path.Combine(_root, "document.json");
        File.WriteAllText(file, "sentinel");
        var refusal = new DocumentEngineException(EngineErrors.PathEscape, "final walk refused");
        Assert.Same(refusal, Assert.Throws<DocumentEngineException>(() => FullPath(file, null, _ => throw refusal)));
    }
}
