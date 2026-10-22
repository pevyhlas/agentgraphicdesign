using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FTPlus.AgentGraphicDesign.Core.Assets;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Export;
using FTPlus.AgentGraphicDesign.Core.Rendering;
using FTPlus.AgentGraphicDesign.Core.Validation;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Assets;

public sealed class VectorAssetProducerTests
{
    private readonly IDocumentEngine _engine = DispatchProxy.Create<IDocumentEngine, EngineProbe>();
    private readonly VectorAssetRequest _request = new()
    {
        Document = new DocumentEngine().CreateDocument("icon", 24, 24),
        SourceName = "inputs/icon.agd.json"
    };
    private readonly EngineProbe _probe;
    private readonly RecordingBudget _budget;
    private readonly VectorAssetProducer _producer;

    public VectorAssetProducerTests()
    {
        _probe = (EngineProbe)_engine;
        _budget = new RecordingBudget(_probe.Calls);
        _producer = new VectorAssetProducer(_engine);
    }

    [Fact]
    public void RequestDefaults_AreEffectiveStrictSvgOptions()
    {
        Assert.Equal(VectorAssetFormat.Svg, _request.Format);
        Assert.False(_request.AsSymbol);
        Assert.True(_request.Strict);
        Assert.Equal(ValidationProfile.Default, _request.Profile);
        Assert.Null(_request.CurrentColor);
        Assert.Equal(1, _request.Scale);
        Assert.Null(_request.Width);
        Assert.Null(_request.Height);
        Assert.False(_request.FontDirectories.IsDefault);
        Assert.Empty(_request.FontDirectories);
        Assert.False(_request.SystemFonts);
        Assert.True(_request.StrictFonts);
    }

    [Fact]
    public void PublicContract_HasTypedBorrowedInputsAndUncopiedTransferViews()
    {
        Assert.Equal(GenericParameterAttributes.Contravariant,
            typeof(IAssetProducer<>).GetGenericArguments()[0].GenericParameterAttributes);
        var method = Assert.Single(typeof(IAssetProducer<VectorAssetRequest>).GetMethods());
        Assert.Equal(typeof(Task<AssetProducerResult>), method.ReturnType);
        Assert.Equal([typeof(VectorAssetRequest), typeof(IAssetProductionBudget), typeof(CancellationToken)],
            method.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.True(method.GetParameters()[2].HasDefaultValue);
        Assert.Equal(["ChargeNodes", "ChargeRasterPixels"],
            typeof(IAssetProductionBudget).GetMethods().Select(member => member.Name).Order(StringComparer.Ordinal));
        Assert.Equal(typeof(int), typeof(IAssetProductionBudget).GetMethod("ChargeNodes")!.GetParameters()[0].ParameterType);
        Assert.Equal(typeof(long), typeof(IAssetProductionBudget).GetMethod("ChargeRasterPixels")!.GetParameters()[0].ParameterType);
        Assert.True(typeof(VectorAssetRequest).IsSealed);
        var properties = typeof(VectorAssetRequest).GetProperties();
        Assert.Equal(new[] { "Document", "SourceName", "Format", "AsSymbol", "Strict", "Profile", "CurrentColor",
            "Scale", "Width", "Height", "FontDirectories", "SystemFonts", "StrictFonts" }.Order(StringComparer.Ordinal),
            properties.Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(["Document", "SourceName"], properties
            .Where(property => property.IsDefined(typeof(RequiredMemberAttribute)))
            .Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.All(properties, property => Assert.Contains(typeof(IsExternalInit),
            property.SetMethod!.ReturnParameter.GetRequiredCustomModifiers()));
        Assert.Equal(typeof(ImmutableArray<string>), typeof(VectorAssetRequest).GetProperty("FontDirectories")!.PropertyType);

        byte[] storage = [0, 1, 2, 3];
        IReadOnlyList<RenderDiagnostic> diagnostics = [new("NOTE", "message")];
        var (data, notes) = new AssetProducerResult(storage.AsMemory(1, 2), diagnostics);
        Assert.True(MemoryMarshal.TryGetArray(data, out var segment));
        Assert.Same(storage, segment.Array);
        Assert.Equal(1, segment.Offset);
        Assert.Equal(2, segment.Count);
        Assert.Same(diagnostics, notes);
    }

    [Fact]
    public async Task RequiredDependenciesAndInputs_AreNullGuarded()
    {
        Assert.Equal([typeof(IDocumentEngine), typeof(IAssetProducer<VectorAssetRequest>)],
            Assert.Single(typeof(AssetPipeline).GetConstructors()).GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Throws<ArgumentNullException>(() => new VectorAssetProducer(null!));
        Assert.Throws<ArgumentNullException>(() => new AssetPipeline(null!, _producer));
        Assert.Throws<ArgumentNullException>(() => new AssetPipeline(_engine, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _producer.ProduceAsync(null!, _budget));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _producer.ProduceAsync(_request, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _producer.ProduceAsync(_request with { Document = null! }, _budget));
        Assert.Empty(_probe.Calls);
    }

    [Theory]
    [InlineData("null-source")]
    [InlineData("empty-source")]
    [InlineData("blank-source")]
    [InlineData("negative-format")]
    [InlineData("unknown-format")]
    [InlineData("negative-profile")]
    [InlineData("unknown-profile")]
    [InlineData("invalid-color")]
    [InlineData("default-fonts")]
    [InlineData("null-font")]
    [InlineData("blank-font")]
    [InlineData("svg-scale")]
    [InlineData("svg-width")]
    [InlineData("svg-height")]
    [InlineData("svg-fonts")]
    [InlineData("raster-color")]
    [InlineData("raster-symbol")]
    public async Task InvalidEffectiveRequests_FailBeforeAnyEngineWork(string option)
    {
        var request = option switch
        {
            "null-source" => _request with { SourceName = null! },
            "empty-source" => _request with { SourceName = "" },
            "blank-source" => _request with { SourceName = " \t" },
            "negative-format" => _request with { Format = (VectorAssetFormat)(-1) },
            "unknown-format" => _request with { Format = (VectorAssetFormat)4 },
            "negative-profile" => _request with { Profile = (ValidationProfile)(-1) },
            "unknown-profile" => _request with { Profile = (ValidationProfile)2 },
            "invalid-color" => _request with { CurrentColor = "red" },
            "default-fonts" => _request with { FontDirectories = default },
            "null-font" => _request with { Format = VectorAssetFormat.Png, FontDirectories = [null!] },
            "blank-font" => _request with { Format = VectorAssetFormat.Png, FontDirectories = [" "] },
            "svg-scale" => _request with { Scale = 2 },
            "svg-width" => _request with { Width = 24 },
            "svg-height" => _request with { Height = 24 },
            "svg-fonts" => _request with { FontDirectories = ["fonts"] },
            "raster-color" => _request with { Format = VectorAssetFormat.Png, CurrentColor = "#000" },
            "raster-symbol" => _request with { Format = VectorAssetFormat.Png, AsSymbol = true },
            _ => throw new InvalidOperationException(option)
        };

        var error = await Assert.ThrowsAsync<DocumentEngineException>(() => _producer.ProduceAsync(request, _budget));

        Assert.Equal(EngineErrors.InvalidArguments, error.Code);
        Assert.Empty(_probe.Calls);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(0)]
    [InlineData(0.099)]
    [InlineData(4.001)]
    public async Task InvalidScale_IsRejectedBeforeCounting(double scale)
    {
        var error = await Assert.ThrowsAsync<DocumentEngineException>(() => _producer.ProduceAsync(
            _request with { Format = VectorAssetFormat.Png, Scale = scale }, _budget));

        Assert.Equal(EngineErrors.InvalidArguments, error.Code);
        Assert.Empty(_probe.Calls);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(8193, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 8193)]
    public async Task InvalidSizeOverrides_AreRejectedBeforeCounting(int width, int height)
    {
        var error = await Assert.ThrowsAsync<DocumentEngineException>(() => _producer.ProduceAsync(
            _request with { Format = VectorAssetFormat.Png, Width = width, Height = height }, _budget));

        Assert.Equal(EngineErrors.InvalidArguments, error.Code);
        Assert.Empty(_probe.Calls);
    }

    [Fact]
    public async Task FontDirectoryCount_IsBoundedWithoutDoingPathAdmission()
    {
        var request = _request with
        {
            Format = VectorAssetFormat.Png,
            FontDirectories = Enumerable.Range(0, 16).Select(index => $"caller-vetted-{index}").ToImmutableArray()
        };
        _probe.Render = (_, options, _) =>
        {
            Assert.Equal(request.FontDirectories, options.FontDirectories);
            return new RenderResult { Data = [1], MimeType = "image/png" };
        };
        await _producer.ProduceAsync(request, _budget);
        _probe.Calls.Clear();

        var error = await Assert.ThrowsAsync<DocumentEngineException>(() => _producer.ProduceAsync(
            request with { FontDirectories = request.FontDirectories.Add("one-too-many") }, _budget));

        Assert.Equal(EngineErrors.ResourceLimit, error.Code);
        Assert.Equal("Asset pipeline resource limit exceeded", error.Message);
        Assert.Empty(_probe.Calls);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, true, false)]
    public async Task SvgOptions_MapSymbolAndColorButIgnoreEffectiveFontBooleans(bool symbol, bool systemFonts, bool strictFonts)
    {
        using var cancellation = new CancellationTokenSource();
        _probe.Export = (document, options, ct) =>
        {
            Assert.Same(_request.Document, document);
            Assert.Equal(cancellation.Token, ct);
            Assert.Equal(new ExportOptions { Format = "svg", SymbolId = symbol ? "icon" : null, CurrentColor = "#000" }, options);
            return Task.FromResult(new ExportResult { Data = [1, 2], MimeType = "image/svg+xml" });
        };

        var result = await _producer.ProduceAsync(_request with
        {
            AsSymbol = symbol,
            CurrentColor = "#000",
            SystemFonts = systemFonts,
            StrictFonts = strictFonts
        }, _budget, cancellation.Token);

        Assert.Equal([1, 2], result.Data.ToArray());
        Assert.Empty(result.Diagnostics);
        Assert.Equal(["CountNodes", "ChargeNodes", "Validate", "ExportAsync"], _probe.Calls);
        Assert.Empty(_budget.Pixels);
    }

    [Theory]
    [InlineData(VectorAssetFormat.Png, RenderFormat.Png)]
    [InlineData(VectorAssetFormat.Jpeg, RenderFormat.Jpeg)]
    [InlineData(VectorAssetFormat.Webp, RenderFormat.Webp)]
    public async Task RasterOptions_MapExactlyAndChargeRoundedOverridesBeforeRender(VectorAssetFormat format, RenderFormat renderedFormat)
    {
        using var cancellation = new CancellationTokenSource();
        ImmutableArray<string> fonts = ["second", "first"];
        _probe.Render = (document, options, ct) =>
        {
            Assert.Same(_request.Document, document);
            Assert.Equal(cancellation.Token, ct);
            Assert.Equal(renderedFormat, options.Format);
            Assert.Equal(0.5, options.Scale);
            Assert.Equal(5, options.Width);
            Assert.Equal(7, options.Height);
            Assert.Equal(fonts, options.FontDirectories);
            Assert.IsType<ImmutableArray<string>>(options.FontDirectories);
            Assert.True(options.SystemFonts);
            Assert.False(options.StrictFonts);
            Assert.Null(options.Background);
            Assert.Equal(12, Assert.Single(_budget.Pixels));
            return new RenderResult { Data = [3], MimeType = "ignored", Width = int.MaxValue, Height = int.MaxValue };
        };

        await _producer.ProduceAsync(_request with
        {
            Format = format,
            Scale = 0.5,
            Width = 5,
            Height = 7,
            FontDirectories = fonts,
            SystemFonts = true,
            StrictFonts = false
        }, _budget, cancellation.Token);

        Assert.Equal(["CountNodes", "ChargeNodes", "Validate", "ChargeRasterPixels", "Render"], _probe.Calls);
        Assert.Equal(12, Assert.Single(_budget.Pixels));
    }

    [Theory]
    [InlineData(0.5, 0.5, 1, 1)]
    [InlineData(2.5, 3.5, 1, 12)]
    [InlineData(5, 7, 0.1, 1)]
    [InlineData(8192, 1, 1, 8192)]
    [InlineData(2, 3, 4, 96)]
    public async Task RasterCanvasDimensions_UseAwayFromZeroRoundingAndRequestFontDefaults(double width, double height, double scale, long pixels)
    {
        _request.Document.Canvas.Width = width;
        _request.Document.Canvas.Height = height;
        _probe.Render = (_, options, _) =>
        {
            Assert.Equal(scale, options.Scale);
            Assert.Null(options.Width);
            Assert.Null(options.Height);
            Assert.Empty(options.FontDirectories);
            Assert.False(options.SystemFonts);
            Assert.True(options.StrictFonts);
            return new RenderResult { Data = [1], MimeType = "image/png", Width = 0, Height = 0 };
        };

        await _producer.ProduceAsync(_request with { Format = VectorAssetFormat.Png, Scale = scale }, _budget);

        Assert.Equal(pixels, Assert.Single(_budget.Pixels));
    }

    [Theory]
    [InlineData(0.499, 1, 1)]
    [InlineData(1, 0.499, 1)]
    [InlineData(8192.001, 1, 1)]
    [InlineData(1, 8192.001, 1)]
    [InlineData(double.NaN, 1, 1)]
    [InlineData(1, double.PositiveInfinity, 1)]
    [InlineData(double.MaxValue, 1, 4)]
    [InlineData(4096, 1, 4)]
    public async Task InvalidEffectiveDimensions_FailAfterValidationBeforePixelCharge(double width, double height, double scale)
    {
        _request.Document.Canvas.Width = width;
        _request.Document.Canvas.Height = height;

        var error = await Assert.ThrowsAsync<DocumentEngineException>(() => _producer.ProduceAsync(
            _request with { Format = VectorAssetFormat.Png, Scale = scale }, _budget));

        Assert.Equal(EngineErrors.InvalidDimensions, error.Code);
        Assert.Equal("Effective raster dimensions must be 1-8192 pixels", error.Message);
        Assert.Equal(["CountNodes", "ChargeNodes", "Validate"], _probe.Calls);
    }

    [Theory]
    [InlineData(VectorAssetFormat.Svg)]
    [InlineData(VectorAssetFormat.Png)]
    public async Task DocumentIdentityAndPrecision_ArePreservedThroughEveryEngineCall(VectorAssetFormat format)
    {
        _request.Document.Canvas.Width = 24.123456789;
        var root = _request.Document.Root;
        _probe.Count = document =>
        {
            Assert.Same(_request.Document, document);
            Assert.Same(root, document.Root);
            Assert.Equal(24.123456789, document.Canvas.Width);
            return 42;
        };
        _probe.Validate = (document, options) =>
        {
            Assert.Same(_request.Document, document);
            Assert.Equal(ValidationProfile.WebIcon, options.Profile);
            Assert.Equal(42, Assert.Single(_budget.Nodes));
            return new ValidationResult();
        };
        _probe.Export = (document, _, _) =>
        {
            Assert.Same(_request.Document, document);
            Assert.Equal(24.123456789, document.Canvas.Width);
            return Task.FromResult(new ExportResult { Data = [1], MimeType = "image/svg+xml" });
        };
        _probe.Render = (document, _, _) =>
        {
            Assert.Same(_request.Document, document);
            Assert.Equal(24.123456789, document.Canvas.Width);
            return new RenderResult { Data = [1], MimeType = "image/png" };
        };

        await _producer.ProduceAsync(_request with { Format = format, Profile = ValidationProfile.WebIcon }, _budget);

        Assert.Equal(24.123456789, _request.Document.Canvas.Width);
        Assert.Same(root, _request.Document.Root);
        Assert.Equal(42, Assert.Single(_budget.Nodes));
    }

    [Fact]
    public async Task RealNodeCount_IncludesRootAndNestedChildrenBeforeSemanticValidation()
    {
        _request.Document.Root.Children = [new GroupNode { Children = [new RectangleNode(), new EllipseNode { Order = "a1" }] }];
        _request.Document.Canvas.Width = 0;

        var error = await Assert.ThrowsAsync<DocumentEngineException>(() =>
            new VectorAssetProducer(new DocumentEngine()).ProduceAsync(_request, _budget));

        Assert.Equal(4, Assert.Single(_budget.Nodes));
        Assert.Equal("CANVAS001", error.Code);
        Assert.Empty(_budget.Pixels);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedCharges_StopBeforeValidationOrRender(bool pixels)
    {
        var failure = new DocumentEngineException(EngineErrors.ResourceLimit, "Asset pipeline resource limit exceeded");
        _budget.NodeFailure = pixels ? null : failure;
        _budget.PixelFailure = pixels ? failure : null;
        _probe.Count = _ => AssetPipeline.MaxNodes + 1;

        var error = await Assert.ThrowsAsync<DocumentEngineException>(() =>
            _producer.ProduceAsync(_request with { Format = VectorAssetFormat.Png }, _budget));

        Assert.Same(failure, error);
        Assert.Equal(pixels ? ["CountNodes", "ChargeNodes", "Validate", "ChargeRasterPixels"]
            : new[] { "CountNodes", "ChargeNodes" }, _probe.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValidationErrors_PrecedeWarningsAndKeepSourceAndPath(bool strict)
    {
        _probe.Validate = (_, _) => new ValidationResult
        {
            Errors = { new("FIRST_ERROR", "first error", "root"), new("SECOND_ERROR", "second error") },
            Warnings = { new("WARNING", "warning", "canvas") }
        };

        var error = await Assert.ThrowsAsync<DocumentEngineException>(() =>
            _producer.ProduceAsync(_request with { Strict = strict }, _budget));

        Assert.Equal("FIRST_ERROR", error.Code);
        Assert.Equal("inputs/icon.agd.json: first error (root)", error.Message);
        Assert.Equal(["CountNodes", "ChargeNodes", "Validate"], _probe.Calls);
    }

    [Fact]
    public async Task StrictDefault_RejectsFirstWarningWhileNonStrictStillValidates()
    {
        _probe.Validate = (_, _) => new ValidationResult
        {
            Warnings = { new("FIRST_WARNING", "first warning", "root"), new("SECOND_WARNING", "second warning") }
        };

        var error = await Assert.ThrowsAsync<DocumentEngineException>(() => _producer.ProduceAsync(_request, _budget));
        Assert.Equal("FIRST_WARNING", error.Code);
        Assert.Equal("inputs/icon.agd.json: first warning (root)", error.Message);
        Assert.Equal(["CountNodes", "ChargeNodes", "Validate"], _probe.Calls);
        _probe.Calls.Clear();

        var result = await _producer.ProduceAsync(_request with { Strict = false }, _budget);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(["CountNodes", "ChargeNodes", "Validate", "ExportAsync"], _probe.Calls);
    }

    [Fact]
    public async Task InvalidSymbolName_KeepsExistingErrorAfterValidation()
    {
        _request.Document.Name = "invalid name";

        var error = await Assert.ThrowsAsync<DocumentEngineException>(() =>
            _producer.ProduceAsync(_request with { AsSymbol = true }, _budget));

        Assert.Equal(EngineErrors.InvalidProperty, error.Code);
        Assert.Equal("Invalid sprite document name: invalid name", error.Message);
        Assert.Equal(["CountNodes", "ChargeNodes", "Validate"], _probe.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CountingAndValidationExceptions_AreOutsideExportCatch(bool validating)
    {
        foreach (var failure in new Exception[] { new InvalidOperationException("unexpected"), new IOException("io"),
            new ArgumentException("argument"), new NotSupportedException("unsupported"),
            new DocumentEngineException("HANDLED", "handled"), new OperationCanceledException(), new OutOfMemoryException() })
        {
            _probe.Count = _ => validating ? 1 : throw failure;
            _probe.Validate = (_, _) => throw failure;

            var error = await Assert.ThrowsAnyAsync<Exception>(() => _producer.ProduceAsync(_request, _budget));

            Assert.Same(failure, error);
        }
        Assert.DoesNotContain("ExportAsync", _probe.Calls);
        Assert.DoesNotContain("Render", _probe.Calls);
    }

    [Theory]
    [InlineData(VectorAssetFormat.Svg)]
    [InlineData(VectorAssetFormat.Png)]
    public async Task UnexpectedExportAndRenderFailures_AreSourceQualified(VectorAssetFormat format)
    {
        foreach (var failure in new Exception[] { new InvalidOperationException("unexpected"), new IOException("io"),
            new ArgumentException("argument"), new NotSupportedException("unsupported") })
        {
            _probe.Export = (_, _, _) => Task.FromException<ExportResult>(failure);
            _probe.Render = (_, _, _) => throw failure;

            var error = await Assert.ThrowsAsync<DocumentEngineException>(() =>
                _producer.ProduceAsync(_request with { Format = format }, _budget));

            Assert.Equal("ASSET_EXPORT_FAILED", error.Code);
            Assert.Equal($"inputs/icon.agd.json: {failure.Message}", error.Message);
        }
    }

    [Theory]
    [InlineData(VectorAssetFormat.Svg)]
    [InlineData(VectorAssetFormat.Png)]
    public async Task HandledExportAndRenderFailures_CancellationAndOutOfMemoryPassThrough(VectorAssetFormat format)
    {
        foreach (var failure in new Exception[] { new DocumentEngineException("HANDLED", "handled"),
            new OperationCanceledException(), new OutOfMemoryException() })
        {
            _probe.Export = (_, _, _) => Task.FromException<ExportResult>(failure);
            _probe.Render = (_, _, _) => throw failure;

            var error = await Assert.ThrowsAnyAsync<Exception>(() =>
                _producer.ProduceAsync(_request with { Format = format }, _budget));

            Assert.Same(failure, error);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_IsCheckedBeforeCountingAndAfterValidation(bool duringValidation)
    {
        using var cancellation = new CancellationTokenSource();
        if (duringValidation)
            _probe.Validate = (_, _) => { cancellation.Cancel(); return new ValidationResult(); };
        else
            cancellation.Cancel();

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _producer.ProduceAsync(_request, _budget, cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(duringValidation ? ["CountNodes", "ChargeNodes", "Validate"] : Array.Empty<string>(), _probe.Calls);
    }

    [Theory]
    [InlineData(VectorAssetFormat.Svg)]
    [InlineData(VectorAssetFormat.Png)]
    public async Task Results_TransferOriginalBytesAndDoNotReadOrMapRendererDiagnostics(VectorAssetFormat format)
    {
        byte[] bytes = [1, 2, 3];
        var diagnostics = new UnconsumedDiagnostics();
        _probe.Export = (_, _, _) => Task.FromResult(new ExportResult { Data = bytes, MimeType = "image/svg+xml" });
        _probe.Render = (_, _, _) => new RenderResult { Data = bytes, MimeType = "image/png", Diagnostics = diagnostics };

        var result = await _producer.ProduceAsync(_request with { Format = format }, _budget);

        Assert.True(MemoryMarshal.TryGetArray(result.Data, out var segment));
        Assert.Same(bytes, segment.Array);
        Assert.Equal(0, segment.Offset);
        Assert.Equal(bytes.Length, segment.Count);
        if (format == VectorAssetFormat.Png)
            Assert.Same(diagnostics, result.Diagnostics);
        else
            Assert.Empty(result.Diagnostics);
    }

    public class EngineProbe : DispatchProxy
    {
        public List<string> Calls { get; } = [];
        public Func<Document, int> Count { get; set; } = _ => 1;
        public Func<Document, ValidationOptions, ValidationResult> Validate { get; set; } = (_, _) => new();
        public Func<Document, ExportOptions, CancellationToken, Task<ExportResult>> Export { get; set; } =
            (_, _, _) => Task.FromResult(new ExportResult { Data = [1], MimeType = "image/svg+xml" });
        public Func<Document, RenderOptions, CancellationToken, RenderResult> Render { get; set; } =
            (_, _, _) => new RenderResult { Data = [1], MimeType = "image/png" };

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Calls.Add(targetMethod!.Name);
            return targetMethod.Name switch
            {
                nameof(IDocumentEngine.CountNodes) => Count((Document)args![0]!),
                nameof(IDocumentEngine.Validate) => Validate((Document)args![0]!, (ValidationOptions)args[1]!),
                nameof(IDocumentEngine.ExportAsync) => Export((Document)args![0]!, (ExportOptions)args[1]!, (CancellationToken)args[2]!),
                nameof(IDocumentEngine.Render) => Render((Document)args![0]!, (RenderOptions)args[1]!, (CancellationToken)args[2]!),
                _ => throw new InvalidOperationException($"Unexpected engine call: {targetMethod.Name}")
            };
        }
    }

    private sealed class RecordingBudget(List<string> calls) : IAssetProductionBudget
    {
        public List<int> Nodes { get; } = [];
        public List<long> Pixels { get; } = [];
        public Exception? NodeFailure { get; set; }
        public Exception? PixelFailure { get; set; }

        public void ChargeNodes(int count)
        {
            calls.Add(nameof(ChargeNodes));
            Nodes.Add(count);
            if (NodeFailure is not null)
                throw NodeFailure;
        }

        public void ChargeRasterPixels(long count)
        {
            calls.Add(nameof(ChargeRasterPixels));
            Pixels.Add(count);
            if (PixelFailure is not null)
                throw PixelFailure;
        }
    }

    private sealed class UnconsumedDiagnostics : IReadOnlyList<RenderDiagnostic>
    {
        public int Count => throw new InvalidOperationException("Diagnostics must be bounded by the host");
        public RenderDiagnostic this[int index] => throw new InvalidOperationException("Diagnostics must be bounded by the host");
        public IEnumerator<RenderDiagnostic> GetEnumerator() => throw new InvalidOperationException("Diagnostics must be bounded by the host");
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
