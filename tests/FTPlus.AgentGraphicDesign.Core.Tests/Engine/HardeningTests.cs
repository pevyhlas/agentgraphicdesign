using System.Globalization;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Rendering;
using FTPlus.AgentGraphicDesign.Core.Serialization;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Engine;

public sealed class HardeningTests : IDisposable
{
    private readonly string _testDir = Path.Combine(Path.GetTempPath(), $"agd-hardening-{Guid.NewGuid():N}");
    private readonly DocumentEngine _engine = new();

    public HardeningTests() => Directory.CreateDirectory(_testDir);

    public void Dispose()
    {
        Directory.Delete(_testDir, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void CreateDocument_NonFiniteDimension_Throws(double value)
    {
        var exception = Assert.Throws<DocumentEngineException>(() =>
            _engine.CreateDocument("finite", value, 100));

        Assert.Equal(EngineErrors.InvalidDimensions, exception.Code);
    }

    [Fact]
    public void CreateDocument_BlankName_Throws()
    {
        var exception = Assert.Throws<DocumentEngineException>(() => _engine.CreateDocument("  "));
        Assert.Equal(EngineErrors.InvalidProperty, exception.Code);
    }

    [Fact]
    public void AddNode_NonFiniteValue_DoesNotMutate()
    {
        var document = _engine.CreateDocument("rollback");

        var exception = Assert.Throws<DocumentEngineException>(() =>
            _engine.AddNode(document, null, new NewNodeSpec { Type = "rectangle", X = double.NaN }));

        Assert.Equal(EngineErrors.InvalidProperty, exception.Code);
        Assert.Empty(document.Root.Children);
        Assert.Equal(1, document.Revn);
    }

    [Fact]
    public void AddNode_PropertyForWrongType_DoesNotMutate()
    {
        var document = _engine.CreateDocument("rollback");

        var exception = Assert.Throws<DocumentEngineException>(() =>
            _engine.AddNode(document, null, new NewNodeSpec { Type = "text", CornerRadius = 2 }));

        Assert.Equal(EngineErrors.InvalidProperty, exception.Code);
        Assert.Empty(document.Root.Children);
        Assert.Equal(1, document.Revn);
    }

    [Fact]
    public void AddNode_ParsesPolygonPointsWithInvariantCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var document = _engine.CreateDocument("points");

            var polygon = Assert.IsType<PolygonNode>(_engine.AddNode(document, null,
                new NewNodeSpec { Type = "polygon", Points = "1.5,2.5 3.25,4.75" }));

            Assert.Equal(1.5, polygon.Points[0].X);
            Assert.Equal(4.75, polygon.Points[1].Y);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void AddNode_MalformedPolygonPoint_DoesNotMutate()
    {
        var document = _engine.CreateDocument("bad-points");

        var exception = Assert.Throws<DocumentEngineException>(() => _engine.AddNode(document, null,
            new NewNodeSpec { Type = "polygon", Points = "1.5,not-a-number" }));

        Assert.Equal(EngineErrors.InvalidProperty, exception.Code);
        Assert.Empty(document.Root.Children);
        Assert.Equal(1, document.Revn);
    }

    [Fact]
    public void UpdateNode_LateTypeFailure_RollsBackEveryProperty()
    {
        var document = _engine.CreateDocument("update-rollback");
        var text = _engine.AddNode(document, null,
            new NewNodeSpec { Type = "text", Name = "original", Text = "hello" });
        var revision = document.Revn;

        var exception = Assert.Throws<DocumentEngineException>(() => _engine.UpdateNode(document, text.Id,
            new NodeUpdates { Name = "changed", Width = 20 }));

        Assert.Equal(EngineErrors.InvalidProperty, exception.Code);
        Assert.Equal("original", text.Name);
        Assert.Equal(revision, document.Revn);
    }

    [Fact]
    public void UpdateNode_NonFiniteCoordinate_DoesNotAllocateTransformOrRevise()
    {
        var document = _engine.CreateDocument("update-finite");
        var rectangle = _engine.AddNode(document, null, new NewNodeSpec { Type = "rectangle" });
        var revision = document.Revn;

        Assert.Throws<DocumentEngineException>(() =>
            _engine.UpdateNode(document, rectangle.Id, new NodeUpdates { X = double.PositiveInfinity }));

        Assert.Null(rectangle.Transform);
        Assert.Equal(revision, document.Revn);
    }

    [Fact]
    public void DocumentIndex_DuplicateIds_Throws()
    {
        var document = _engine.CreateDocument("duplicate");
        var id = Guid.NewGuid();
        document.Root.Children =
        [
            new RectangleNode { Id = id, Order = "a0" },
            new EllipseNode { Id = id, Order = "a1" }
        ];

        var exception = Assert.Throws<DocumentEngineException>(() => DocumentIndex.Build(document));
        Assert.Equal(EngineErrors.InvalidProperty, exception.Code);
        Assert.Contains("Duplicate", exception.Message);
    }

    [Fact]
    public void DocumentIndex_NullChildren_ThrowsStructuredError()
    {
        var document = _engine.CreateDocument("null-children");
        document.Root.Children = null!;

        var exception = Assert.Throws<DocumentEngineException>(() => DocumentIndex.Build(document));
        Assert.Equal(EngineErrors.InvalidProperty, exception.Code);
    }

    [Fact]
    public void ReorderNode_BothAnchorsNull_DoesNotMutate()
    {
        var document = _engine.CreateDocument("anchors");
        var node = _engine.AddNode(document, null, new NewNodeSpec { Type = "rectangle" });
        var revision = document.Revn;
        var order = node.Order;

        var exception = Assert.Throws<DocumentEngineException>(() =>
            _engine.ReorderNode(document, node.Id, null, null));

        Assert.Equal(EngineErrors.InvalidProperty, exception.Code);
        Assert.Equal(order, node.Order);
        Assert.Equal(revision, document.Revn);
    }

    [Fact]
    public void ReorderNode_SelfAnchor_DoesNotMutate()
    {
        var document = _engine.CreateDocument("self-anchor");
        var node = _engine.AddNode(document, null, new NewNodeSpec { Type = "rectangle" });
        var revision = document.Revn;

        var exception = Assert.Throws<DocumentEngineException>(() =>
            _engine.ReorderNode(document, node.Id, node.Id, null));

        Assert.Equal(EngineErrors.InvalidProperty, exception.Code);
        Assert.Equal(revision, document.Revn);
    }

    [Fact]
    public void ReorderNode_ReversedBounds_DoesNotMutate()
    {
        var document = _engine.CreateDocument("reversed");
        var first = _engine.AddNode(document, null, new NewNodeSpec { Type = "rectangle" });
        var second = _engine.AddNode(document, null, new NewNodeSpec { Type = "rectangle" });
        var moving = _engine.AddNode(document, null, new NewNodeSpec { Type = "rectangle" });
        var revision = document.Revn;
        var originalOrder = moving.Order;

        var exception = Assert.Throws<DocumentEngineException>(() =>
            _engine.ReorderNode(document, moving.Id, second.Id, first.Id));

        Assert.Equal(EngineErrors.InvalidProperty, exception.Code);
        Assert.Equal(originalOrder, moving.Order);
        Assert.Equal(revision, document.Revn);
    }

    [Fact]
    public void ReorderNode_DuplicateGeneratedOrder_DoesNotMutate()
    {
        var document = _engine.CreateDocument("duplicate-order");
        var lower = _engine.AddNode(document, null, new NewNodeSpec { Type = "rectangle" });
        var existing = _engine.AddNode(document, null, new NewNodeSpec { Type = "rectangle" });
        var upper = _engine.AddNode(document, null, new NewNodeSpec { Type = "rectangle" });
        var moving = _engine.AddNode(document, null, new NewNodeSpec { Type = "rectangle" });
        lower.Order = "a0";
        existing.Order = "a1";
        upper.Order = "a2";
        moving.Order = "z0";
        var revision = document.Revn;

        var exception = Assert.Throws<DocumentEngineException>(() =>
            _engine.ReorderNode(document, moving.Id, lower.Id, upper.Id));

        Assert.Equal(EngineErrors.InvalidProperty, exception.Code);
        Assert.Equal("z0", moving.Order);
        Assert.Equal(revision, document.Revn);
    }

    [Fact]
    public void ReorderNode_NonDenseBounds_ReturnsStableErrorWithoutMutation()
    {
        var document = _engine.CreateDocument("non-dense");
        var lower = _engine.AddNode(document, null, new NewNodeSpec { Type = "rectangle" });
        var upper = _engine.AddNode(document, null, new NewNodeSpec { Type = "rectangle" });
        var moving = _engine.AddNode(document, null, new NewNodeSpec { Type = "rectangle" });
        lower.Order = "a";
        upper.Order = "a0";
        moving.Order = "z";
        var revision = document.Revn;

        var exception = Assert.Throws<DocumentEngineException>(() =>
            _engine.ReorderNode(document, moving.Id, lower.Id, upper.Id));

        Assert.Equal(EngineErrors.InvalidProperty, exception.Code);
        Assert.Equal("z", moving.Order);
        Assert.Equal(revision, document.Revn);
    }

    [Fact]
    public async Task SaveDocumentToFile_TwoSameRevisionWriters_OnlyOneSucceeds()
    {
        var path = Path.Combine(_testDir, "concurrent.agd.json");
        var initial = _engine.CreateDocument("initial");
        _engine.SaveDocumentToFile(initial, path);
        var first = DocumentSerializer.Deserialize(DocumentSerializer.Serialize(initial))!;
        var second = DocumentSerializer.Deserialize(DocumentSerializer.Serialize(initial))!;
        first.Name = "first";
        second.Name = "second";
        first.Revn = 2;
        second.Revn = 2;
        using var barrier = new Barrier(2);

        async Task<string?> WriteAsync(FTPlus.AgentGraphicDesign.Core.Documents.Document document)
        {
            return await Task.Run(() =>
            {
                barrier.SignalAndWait();
                try
                {
                    _engine.SaveDocumentToFile(document, path, expectedRevn: 1);
                    return null;
                }
                catch (DocumentEngineException exception)
                {
                    return exception.Code;
                }
            });
        }

        var results = await Task.WhenAll(WriteAsync(first), WriteAsync(second));

        Assert.Single(results, result => result is null);
        Assert.Single(results, result => result == EngineErrors.RevisionConflict);
    }

    [Fact]
    public void CreateDocumentFile_ExistingTarget_UsesStableCode()
    {
        var path = Path.Combine(_testDir, "create.agd.json");
        var document = _engine.CreateDocument("create");
        _engine.CreateDocumentFile(document, path);

        var exception = Assert.Throws<DocumentEngineException>(() =>
            _engine.CreateDocumentFile(document, path));

        Assert.Equal(DocumentStoreErrors.DocumentExists, exception.Code);
    }

    [Fact]
    public void SaveDocumentToFile_ExpectedRevision_DoesNotOverwriteCorruptFile()
    {
        var path = Path.Combine(_testDir, "corrupt.agd.json");
        const string corrupt = "{ definitely-not-json }";
        File.WriteAllText(path, corrupt);
        var document = _engine.CreateDocument("replacement");

        var exception = Assert.Throws<DocumentEngineException>(() =>
            _engine.SaveDocumentToFile(document, path, expectedRevn: 1));

        Assert.Equal(EngineErrors.ParseError, exception.Code);
        Assert.Equal(corrupt, File.ReadAllText(path));
    }

    [Fact]
    public void SaveDocumentToFile_LongExpectedRevision_Succeeds()
    {
        var path = Path.Combine(_testDir, "long-revision.agd.json");
        var document = _engine.CreateDocument("long");
        document.Revn = (long)int.MaxValue + 10;
        _engine.SaveDocumentToFile(document, path);
        var expected = document.Revn;
        document.Revn++;

        _engine.SaveDocumentToFile(document, path, expected);

        Assert.Equal(expected + 1, _engine.LoadDocumentFromFile(path)!.Revn);
    }

    [Fact]
    public async Task AddNode_ConcurrentCallsOnSameDocument_AreSerialized()
    {
        var document = _engine.CreateDocument("concurrent-mutation");

        await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => Task.Run(() =>
            _engine.AddNode(document, null, new NewNodeSpec { Type = "rectangle" }))));

        Assert.Equal(100, document.Root.Children.Count);
        Assert.Equal(101, document.Revn);
        Assert.Equal(100, document.Root.Children.Select(node => node.Order).Distinct().Count());
    }

    [Fact]
    public void Mutation_FutureDocumentVersion_IsRejectedWithoutChanges()
    {
        var document = _engine.CreateDocument("future");
        document.Version = Document.SupportedVersion + 1;

        var exception = Assert.Throws<DocumentEngineException>(() =>
            _engine.AddNode(document, null, new NewNodeSpec { Type = "rectangle" }));

        Assert.Equal(EngineErrors.UnsupportedVersion, exception.Code);
        Assert.Empty(document.Root.Children);
        Assert.Equal(1, document.Revn);
    }

    [Fact]
    public void Save_FutureDocumentVersion_IsRejectedWithoutFile()
    {
        var document = _engine.CreateDocument("future-save");
        document.Version = Document.SupportedVersion + 1;
        var path = Path.Combine(_testDir, "future.agd.json");

        var exception = Assert.Throws<DocumentEngineException>(() =>
            _engine.SaveDocumentToFile(document, path));

        Assert.Equal(EngineErrors.UnsupportedVersion, exception.Code);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Mutation_MaximumRevision_IsRejectedBeforeMutation()
    {
        var document = _engine.CreateDocument("revision-limit");
        document.Revn = long.MaxValue;

        var exception = Assert.Throws<DocumentEngineException>(() =>
            _engine.AddNode(document, null, new NewNodeSpec { Type = "rectangle" }));

        Assert.Equal(EngineErrors.RevisionConflict, exception.Code);
        Assert.Empty(document.Root.Children);
        Assert.Equal(long.MaxValue, document.Revn);
    }

    [Fact]
    public void DocumentIndex_InvalidAndDuplicateSiblingOrders_Throw()
    {
        var invalid = _engine.CreateDocument("invalid-order");
        invalid.Root.Children.Add(new RectangleNode { Id = Guid.NewGuid(), Order = "a!" });
        Assert.Throws<DocumentEngineException>(() => DocumentIndex.Build(invalid));

        var duplicate = _engine.CreateDocument("duplicate-order");
        duplicate.Root.Children =
        [
            new RectangleNode { Id = Guid.NewGuid(), Order = "a0" },
            new EllipseNode { Id = Guid.NewGuid(), Order = "a0" }
        ];
        Assert.Throws<DocumentEngineException>(() => DocumentIndex.Build(duplicate));
    }

    [Fact]
    public void AddNode_StrictColorGrammarRejectsOutOfRangeRgbWithoutMutation()
    {
        var document = _engine.CreateDocument("strict-color");

        var exception = Assert.Throws<DocumentEngineException>(() => _engine.AddNode(
            document,
            null,
            new NewNodeSpec { Type = "rectangle", Fill = "rgb(999, 0, 0)" }));

        Assert.Equal(EngineErrors.InvalidProperty, exception.Code);
        Assert.Empty(document.Root.Children);
    }

    [Fact]
    public void AddNode_OversizedTextIsRejectedWithoutMutation()
    {
        var document = _engine.CreateDocument("bounded-text");

        Assert.Throws<DocumentEngineException>(() => _engine.AddNode(
            document,
            null,
            new NewNodeSpec
            {
                Type = "text",
                Text = new string('x', DocumentLimits.MaxTextLength + 1)
            }));

        Assert.Empty(document.Root.Children);
        Assert.Equal(1, document.Revn);
    }

    [Fact]
    public void SaveDocumentToFile_ExpectedRevisionAndMissingTarget_ReportsConflict()
    {
        var document = _engine.CreateDocument("missing-revision");
        var path = Path.Combine(_testDir, "missing.agd.json");

        var exception = Assert.Throws<DocumentEngineException>(() =>
            _engine.SaveDocumentToFile(document, path, expectedRevn: 1));

        Assert.Equal(EngineErrors.RevisionConflict, exception.Code);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void CancellationAwareImportPathAndRender_ThrowBeforeWork()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var token = cancellation.Token;
        var rendererEngine = new DocumentEngine(renderer: new TestRenderer());
        var document = rendererEngine.CreateDocument("cancel");

        Assert.Throws<OperationCanceledException>(() => rendererEngine.ImportSvg("<svg/>", token));
        Assert.Throws<OperationCanceledException>(() => rendererEngine.PathBooleanOp(
            "M0,0",
            "M0,0",
            FTPlus.AgentGraphicDesign.Core.PathOperations.BooleanOperation.Union,
            token));
        Assert.Throws<OperationCanceledException>(() => rendererEngine.Render(document, new RenderOptions(), token));
    }

    private sealed class TestRenderer : IRenderer
    {
        public RenderResult Render(Document document, RenderOptions options) => new()
        {
            Data = [],
            MimeType = "image/png"
        };
    }
}
