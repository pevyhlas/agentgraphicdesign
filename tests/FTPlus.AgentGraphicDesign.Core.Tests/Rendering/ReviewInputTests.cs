using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Platform;
using FTPlus.AgentGraphicDesign.Core.Rendering;
using FTPlus.AgentGraphicDesign.Core.Serialization;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Rendering;

public sealed class ReviewInputTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "agd-review-" + Guid.NewGuid().ToString("N"));
    private static bool InspectionAdmitted => PlatformProfile.Current.GateAdmits(PlatformCapability.AssetPipeline);
    private int _opens;

    public ReviewInputTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("a#b#01234567-89AB-CDEF-0123-456789ABCDEF", "a#b", true)]
    [InlineData("a#0123456789abcdef0123456789abcdef", "a#0123456789abcdef0123456789abcdef", false)]
    [InlineData("a#{01234567-89ab-cdef-0123-456789abcdef}", "a#{01234567-89ab-cdef-0123-456789abcdef}", false)]
    [InlineData("a#01234567-89ab-cdef-0123-456789abcdef ", "a#01234567-89ab-cdef-0123-456789abcdef ", false)]
    [InlineData("a#01234567-89ab-cdef-0123-456789abcdef#tail", "a#01234567-89ab-cdef-0123-456789abcdef#tail", false)]
    [InlineData("a#01234567-89ab-cdef-0123-456789abcdeg", "a#01234567-89ab-cdef-0123-456789abcdeg", false)]
    [InlineData("a#plain=label", "a#plain=label", false)]
    public void GrammarUsesOnlyLastCanonicalGuid(string text, string path, bool node)
    {
        var reference = AssetReference.Parse(text, "separate label");
        Assert.Equal(path, reference.DocumentPath);
        Assert.Equal(node ? Guid.Parse("01234567-89ab-cdef-0123-456789abcdef") : (Guid?)null, reference.NodeId);
        Assert.Equal("separate label", reference.Label);
    }

    [Theory]
    [InlineData("#2563ebff", "#2563eb")]
    [InlineData("#25f", "#2255ff")]
    [InlineData("#25ff", "#2255ff")]
    [InlineData("rgba(37,99,235,0.999)", "#2563eb")]
    [InlineData("#A3B7C9", "#a3b7c9")]
    [InlineData("rgb(2,3,255)", "#0203ff")]
    public void ColorHasIndependentCssByteOracles(string text, string expected) => Assert.Equal(expected, ReviewColor.ParseOpaque(text));

    [Theory]
    [InlineData("rgba(37,99,235,0.998)")]
    [InlineData("#2563ebfe")]
    [InlineData("#ff00")]
    [InlineData("checker")]
    [InlineData(null)]
    public void ReviewColorRequiresOpaqueAlphaByte(string? text) =>
        Assert.Equal(EngineErrors.InvalidProperty, Assert.Throws<DocumentEngineException>(() => ReviewColor.ParseOpaque(text)).Code);

    [Fact]
    public void ByteRoundingIsTiesToEven()
    {
        Assert.Equal(2, ReviewColor.ToByte(2.5 / 255));
        Assert.Equal(4, ReviewColor.ToByte(3.5 / 255));
        Assert.Equal(254, ReviewColor.ToByte(254.5 / 255));
        Assert.Equal(255, ReviewColor.ToByte(0.999));
        Assert.Equal(254, ReviewColor.ToByte(0.998));
    }

    [Fact]
    public async Task OneCaptureSharedAcrossNodeSelectionsHashesOriginalBomAndWhitespace()
    {
        var bytes = Encoding.UTF8.GetBytes("\ufeff \n" + Json() + "\n  ");
        File.WriteAllBytes(Path.Combine(_root, "doc.json"), bytes);
        var inputs = await Reader().ReadAsync([new("doc.json"), new("./doc.json", Guid.NewGuid())], [], _root);
        Assert.Equal(1, _opens);
        Assert.Same(inputs.References[0].Source, inputs.References[1].Source);
        Assert.Equal(bytes.Length, inputs.CountedBytes);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), inputs.References[0].Source.Sha256);
        Assert.Equal("doc.json", inputs.References[0].Source.OutputPath);
        Assert.Equal("capture", inputs.References[0].Source.Document.Name);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AggregateChecksAllDocumentsAndFontsBeforeOpening(bool withFonts)
    {
        Write("one.json", Json());
        Write("two.json", Json());
        Directory.CreateDirectory(Path.Combine(_root, "fonts"));
        Write("fonts/a.ttf", "abcd");
        var length = new FileInfo(Path.Combine(_root, "one.json")).Length + new FileInfo(Path.Combine(_root, "two.json")).Length + (withFonts ? 4 : 0);
        string[] fonts = withFonts ? ["fonts"] : [];
        var result = await Reader(limit: length).ReadAsync([new("one.json"), new("two.json"), new("one.json")], fonts, _root);
        Assert.Equal(length, result.CountedBytes);
        Assert.Equal(2, _opens);
        _opens = 0;
        await Error("INPUT_TOO_LARGE", () => Reader(limit: length - 1).ReadAsync([new("one.json"), new("two.json")], fonts, _root));
        Assert.Equal(0, _opens);
    }

    [Fact]
    public async Task PerDocumentLimitAcceptsExactBytesAndRejectsOneOverBeforeOpen()
    {
        var bytes = Encoding.UTF8.GetBytes(Json());
        using (var file = File.Create(Path.Combine(_root, "doc.json")))
        {
            file.Write(bytes);
            var padding = Enumerable.Repeat((byte)' ', 16 * 1024 * 1024 - bytes.Length).ToArray();
            file.Write(padding);
        }
        var result = await Reader().ReadAsync([new("doc.json")], [], _root);
        Assert.Equal(16 * 1024 * 1024, result.CountedBytes);
        File.AppendAllText(Path.Combine(_root, "doc.json"), " ");
        _opens = 0;
        await Error("INPUT_TOO_LARGE", () => Reader().ReadAsync([new("doc.json")], [], _root));
        Assert.Equal(0, _opens);
    }

    [Fact]
    public async Task RealAggregateLimitAccepts64MiBAndRejectsExtraFontBeforeOpen()
    {
        var bytes = Encoding.UTF8.GetBytes(Json().PadRight(16 * 1024 * 1024));
        for (var i = 0; i < 4; i++) File.WriteAllBytes(Path.Combine(_root, $"{i}.json"), bytes);
        AssetReference[] references = [new("0.json"), new("1.json"), new("2.json"), new("3.json"), new("0.json")];
        var result = await Reader().ReadAsync(references, [], _root);
        Assert.Equal(64 * 1024 * 1024, result.CountedBytes);
        Assert.Equal(4, _opens);
        Directory.CreateDirectory(Path.Combine(_root, "fonts"));
        Write("fonts/a.ttf", "x");
        _opens = 0;
        await Error("INPUT_TOO_LARGE", () => Reader().ReadAsync(references, ["fonts"], _root));
        Assert.Equal(0, _opens);
    }

    [Fact]
    public async Task ActualCaptureLengthIsRecheckedAndFailedStreamIsDisposed()
    {
        Write("doc.json", Json());
        var size = new FileInfo(Path.Combine(_root, "doc.json")).Length;
        var stream = new TrackingStream(Encoding.UTF8.GetBytes(Json() + " "));
        var reader = new ReferenceReader(false, size, _ => stream);
        await Error("INPUT_TOO_LARGE", () => reader.ReadAsync([new("doc.json")], [], _root));
        Assert.True(stream.Disposed);
    }

    [Fact]
    public async Task CancellationDuringCaptureDisposesTheOnlyOpenedStream()
    {
        Write("doc.json", Json());
        Write("later.json", Json());
        using var cancellation = new CancellationTokenSource();
        var stream = new PausingStream(Encoding.UTF8.GetBytes(Json()));
        var reader = new ReferenceReader(false, ReferenceReader.MaxInputBytes, _ =>
        {
            _opens++;
            return stream;
        });
        var read = reader.ReadAsync([new("doc.json"), new("later.json")], [], _root, cancellation.Token);
        await stream.SecondRead.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        Assert.Equal(16, stream.BytesReturned);
        Assert.True(stream.Disposed);
        Assert.Equal(1, _opens);
    }

    [Fact]
    public async Task CaptureIsParsedInsteadOfReopeningFile()
    {
        Write("doc.json", "not json");
        var bytes = Encoding.UTF8.GetBytes(Json());
        var reader = new ReferenceReader(false, ReferenceReader.MaxInputBytes, _ => new MemoryStream(bytes));
        var result = await reader.ReadAsync([new("doc.json")], [], _root);
        Assert.Equal("capture", result.References[0].Source.Document.Name);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), result.References[0].Source.Sha256);
        Assert.Equal(bytes.Length, result.CountedBytes);
    }

    [Fact]
    public async Task OriginalDuplicatePropertiesAndMalformedInputsRefuse()
    {
        Write("doc.json", Json().Replace("\"name\": \"capture\"", "\"name\": \"first\", \"name\": \"capture\"", StringComparison.Ordinal));
        await Error(EngineErrors.ParseError, () => Reader().ReadAsync([new("doc.json")], [], _root));
        Write("doc.json", "{");
        await Error(EngineErrors.ParseError, () => Reader().ReadAsync([new("doc.json")], [], _root));
    }

    [Fact]
    public async Task NativeRefusalOrderIsLexicalThenDirectoryThenContent()
    {
        await Error("REFERENCE_KIND_UNSUPPORTED", () => Reader().ReadAsync([new("missing/asset.agd-pixels.json")], [], _root));
        Assert.Equal(0, _opens);
        var native = Directory.CreateDirectory(Path.Combine(_root, "native")).FullName;
        File.WriteAllText(Path.Combine(native, "asset.agd-pixels.json"), "invalid");
        await Error("REFERENCE_KIND_UNSUPPORTED", () => Reader().ReadAsync([new("native")], [], _root));
        Assert.Equal(0, _opens);
        Directory.CreateDirectory(Path.Combine(_root, "ordinary"));
        await Error(EngineErrors.PathEscape, () => Reader().ReadAsync([new("ordinary")], [], _root));
        Write("renamed.json", "\ufeff{\"schema\":\"agd.native-pixels\"}");
        await Error("REFERENCE_KIND_UNSUPPORTED", () => Reader().ReadAsync([new("renamed.json")], [], _root));
        Assert.Equal(1, _opens);
    }

    [Fact]
    public async Task MissingAndCancellationDoNotOpenAndDoNotLeakAbsolutePaths()
    {
        await Error(EngineErrors.DocumentNotFound, () => Reader().ReadAsync([new("missing")], [], _root));
        Write("doc.json", Json());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Reader().ReadAsync([new("doc.json")], [], _root, new CancellationToken(true)));
        Assert.Equal(0, _opens);
        Assert.Null(ReferenceReader.RelativePath(_root, Path.Combine(Path.GetDirectoryName(_root)!, "outside.json")));
        Assert.Equal("sub/doc.json", ReferenceReader.RelativePath(_root, Path.Combine(_root, "sub", "doc.json")));
    }

    [InspectionFact]
    public async Task AdmittedReferencesRejectLinksIncludingHiddenTraversalAndNativeChildLinks()
    {
        Write("doc.json", Json());
        File.CreateSymbolicLink(Path.Combine(_root, "link.json"), Path.Combine(_root, "doc.json"));
        await Error(EngineErrors.PathEscape, () => Reader().ReadAsync([new("link.json")], [], _root));
        Directory.CreateDirectory(Path.Combine(_root, "real"));
        Directory.CreateSymbolicLink(Path.Combine(_root, "linkdir"), Path.Combine(_root, "real"));
        await Error(EngineErrors.PathEscape, () => Reader().ReadAsync([new("linkdir/../doc.json")], [], _root));
        File.CreateSymbolicLink(Path.Combine(_root, "real", "asset.agd-pixels.json"), Path.Combine(_root, "missing"));
        await Error(EngineErrors.PathEscape, () => Reader().ReadAsync([new("real")], [], _root));
        Assert.Equal(0, _opens);
    }

    [LinuxInspectionFact]
    public async Task LinuxDeviceIsRejectedBeforeOpen()
    {
        await Error(EngineErrors.PathEscape, () => Reader().ReadAsync([new("/dev/null")], [], _root));
        Assert.Equal(0, _opens);
    }

    [LinuxInspectionFact]
    public async Task GenuineDeviceFontDirectoryEntriesRefuseBeforeDocumentOpen()
    {
        // devpts contains real character devices, including ptmx, without requiring CAP_MKNOD
        // to create a device fixture. No device is opened; the all-entry font pre-pass refuses it.
        Assert.Contains("/dev/pts/ptmx", Directory.EnumerateFileSystemEntries("/dev/pts"));
        Assert.All(Directory.EnumerateFileSystemEntries("/dev/pts"), path =>
            Assert.Equal((FileAttributes)0, File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)));
        Write("doc.json", Json());
        await Error(EngineErrors.PathEscape, () => Reader().ReadAsync([new("doc.json")], ["/dev/pts"], _root));
        Assert.Equal(0, _opens);
    }

    [LinuxInspectionTheory]
    [InlineData(false, "pipe.txt")]
    [InlineData(true, "pipe.txt")]
    [InlineData(true, "x.ttf")]
    public async Task FifoReferenceAndFontEntriesRefuseWithBoundedWatchdog(bool fontEntry, string name)
    {
        Write("doc.json", Json());
        var fonts = Directory.CreateDirectory(Path.Combine(_root, "fonts")).FullName;
        var path = Path.Combine(fontEntry ? fonts : _root, name);
        Assert.Equal(0, MakeFifo(path, 0x180));
        // O_RDWR|O_NONBLOCK keeps a write end alive. A mistaken read can be cancelled rather
        // than hanging in open(2), and the independent open-count assertion still fails it.
        var fd = Open(path, 2 | 0x800);
        Assert.True(fd >= 0);
        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await Error(EngineErrors.PathEscape, () => Reader().ReadAsync([new(fontEntry ? "doc.json" : name)], fontEntry ? ["fonts"] : [], _root, cancellation.Token));
            Assert.Equal(0, _opens);
        }
        finally { Close(fd); }
    }

    private ReferenceReader Reader(long limit = ReferenceReader.MaxInputBytes) => new(InspectionAdmitted, limit, path =>
    {
        _opens++;
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
    });

    private async Task Error(string code, Func<Task<ReferenceInputs>> operation)
    {
        var error = await Assert.ThrowsAsync<DocumentEngineException>(operation);
        Assert.Equal(code, error.Code);
        Assert.DoesNotContain(_root, error.Message);
    }

    private void Write(string name, string content) => File.WriteAllText(Path.Combine(_root, name), content);
    private static string Json() => DocumentSerializer.Serialize(new Document { Name = "capture" });
    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class TrackingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    private sealed class PausingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public TaskCompletionSource SecondRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int BytesReturned { get; private set; }
        public bool Disposed { get; private set; }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (BytesReturned == 0)
            {
                BytesReturned = await base.ReadAsync(buffer[..16], cancellationToken);
                return BytesReturned;
            }
            SecondRead.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MakeFifo([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mode);
    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
    [DllImport("libc", EntryPoint = "close")]
    private static extern int Close(int fd);
}
