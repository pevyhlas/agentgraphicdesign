using System.Text.Json;
using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Patching;
using FTPlus.AgentGraphicDesign.Core.Serialization;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Engine;

public class PatchTests : IDisposable
{
    private readonly string _testDir;
    private readonly DocumentEngine _engine = new();

    public PatchTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"agd-patch-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, true);
        }

        GC.SuppressFinalize(this);
    }

    // ---- Parsing ----

    [Fact]
    public void Parse_AcceptsDiscriminatorInAnyPosition()
    {
        var id = Guid.NewGuid();
        var result = PatchOperationParser.Parse($$"""
            [{ "nodeId": "{{id}}", "path": "/width", "value": 10, "op": "set" }]
            """);

        Assert.True(result.Success, result.Error?.Message);
        var set = Assert.IsType<SetOperation>(Assert.Single(result.Operations!));
        Assert.Equal(id, set.NodeId);
        Assert.Equal("/width", set.Path);
        Assert.Equal(10, set.Value.GetDouble());
    }

    [Theory]
    [InlineData("""[{ "op": "teleport", "nodeId": "00000000-0000-0000-0000-000000000001" }]""", "Unknown op 'teleport'")]
    [InlineData("""[{ "nodeId": "00000000-0000-0000-0000-000000000001" }]""", "no 'op'")]
    [InlineData("""[{ "op": "set", "nodeId": "00000000-0000-0000-0000-000000000001", "path": "/width" }]""", "malformed")]
    [InlineData("""[{ "op": "deleteNode", "nodeId": "00000000-0000-0000-0000-000000000001", "extra": 1 }]""", "malformed")]
    [InlineData("""[{ "op": "deleteNode", "nodeId": "not-a-guid" }]""", "malformed")]
    [InlineData("""[42]""", "must be a JSON object")]
    public void Parse_RejectsBadOperationWithIndex(string json, string expectedFragment)
    {
        var result = PatchOperationParser.Parse(json);

        Assert.False(result.Success);
        Assert.Equal(EngineErrors.InvalidArguments, result.Error!.Code);
        Assert.Equal(0, result.Error.OperationIndex);
        Assert.Contains(expectedFragment, result.Error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("[ { \"op\": \"deleteNode\", \"nodeId\": \"00000000-0000-0000-0000-000000000001\" }, ")]
    public void Parse_RejectsNonArrayInput(string json)
    {
        var result = PatchOperationParser.Parse(json);

        Assert.False(result.Success);
        Assert.Equal(EngineErrors.InvalidArguments, result.Error!.Code);
        Assert.Null(result.Error.OperationIndex);
    }

    [Fact]
    public void Parse_RejectsMoreThanMaximumOperations()
    {
        var operation = $$"""{ "op": "deleteNode", "nodeId": "{{Guid.NewGuid()}}" }""";
        var json = "[" + string.Join(",", Enumerable.Repeat(operation, PatchLimits.MaxOperations + 1)) + "]";

        var result = PatchOperationParser.Parse(json);

        Assert.False(result.Success);
        Assert.Equal(EngineErrors.ResourceLimit, result.Error!.Code);
    }

    [Fact]
    public void Parse_RejectsDuplicateProperties()
    {
        var result = PatchOperationParser.Parse($$"""
            [{ "op": "deleteNode", "nodeId": "{{Guid.NewGuid()}}", "nodeId": "{{Guid.NewGuid()}}" }]
            """);

        Assert.False(result.Success);
        Assert.Contains("Duplicate", result.Error!.Message);
    }

    [Fact]
    public void Parse_SetCanvasDistinguishesAbsentFromNullBackground()
    {
        var absent = Assert.IsType<SetCanvasOperation>(Ops("""[{ "op": "setCanvas", "width": 10 }]""")[0]);
        var cleared = Assert.IsType<SetCanvasOperation>(Ops("""[{ "op": "setCanvas", "background": null }]""")[0]);

        Assert.Equal(JsonValueKind.Undefined, absent.Background.ValueKind);
        Assert.Equal(JsonValueKind.Null, cleared.Background.ValueKind);
    }

    // ---- Each operation ----

    [Fact]
    public void Set_UpdatesNestedStyleTransformAndDimensions()
    {
        var (path, handle, rect) = CreateDocumentWithRectangle();

        var result = _engine.ApplyPatch(handle, Ops($$"""
            [
              { "op": "set", "nodeId": "{{rect}}", "path": "/style/fill/color", "value": "#ff0000" },
              { "op": "set", "nodeId": "{{rect}}", "path": "/style/stroke/width", "value": 3.5 },
              { "op": "set", "nodeId": "{{rect}}", "path": "/style/stroke/dashArray", "value": [4, 2] },
              { "op": "set", "nodeId": "{{rect}}", "path": "/style/blendMode", "value": "multiply" },
              { "op": "set", "nodeId": "{{rect}}", "path": "/transform/rotation", "value": 45 },
              { "op": "set", "nodeId": "{{rect}}", "path": "/transform/scaleX", "value": 2 },
              { "op": "set", "nodeId": "{{rect}}", "path": "/width", "value": 250 },
              { "op": "set", "nodeId": "{{rect}}", "path": "/name", "value": "hero" }
            ]
            """));

        AssertSuccess(result);
        Assert.Equal(8, result.Applied);
        var saved = Assert.IsType<RectangleNode>(Assert.Single(Reload(path).Root.Children));
        Assert.Equal("#ff0000", saved.Style!.Fill!.Color);
        Assert.Equal(3.5, saved.Style.Stroke!.Width);
        Assert.Equal([4, 2], saved.Style.Stroke.DashArray!);
        Assert.Equal("multiply", saved.Style.BlendMode);
        Assert.Equal(45, saved.Transform!.Rotation);
        Assert.Equal(2, saved.Transform.ScaleX);
        Assert.Equal(250, saved.Width);
        Assert.Equal("hero", saved.Name);
        Assert.Equal(rect, saved.Id);
    }

    [Fact]
    public void Set_GradientFillRoundTripsThroughPolymorphicSerializer()
    {
        var (path, handle, rect) = CreateDocumentWithRectangle();

        var result = _engine.ApplyPatch(handle, Ops($$"""
            [{ "op": "set", "nodeId": "{{rect}}", "path": "/style/fill/gradient",
               "value": { "type": "linear", "x2": 0, "y2": 1,
                          "stops": [ { "offset": 0, "color": "#000000" }, { "offset": 1, "color": "#ffffff" } ] } }]
            """));

        AssertSuccess(result);
        var saved = Assert.IsType<RectangleNode>(Assert.Single(Reload(path).Root.Children));
        var gradient = Assert.IsType<LinearGradient>(saved.Style!.Fill!.Gradient);
        Assert.Equal(1, gradient.Y2);
        Assert.Equal(2, gradient.Stops.Count);
    }

    [Fact]
    public void Set_PointerIntoArraysByIndexAndAppend()
    {
        var (path, handle) = CreateDocument();
        var polygon = _engine.AddNode(handle.Document, null, new NewNodeSpec { Type = "polygon", Points = "0,0 10,0 5,8" });
        _engine.Save(handle);

        var result = _engine.ApplyPatch(handle, Ops($$"""
            [
              { "op": "set", "nodeId": "{{polygon.Id}}", "path": "/points/1/x", "value": 20 },
              { "op": "set", "nodeId": "{{polygon.Id}}", "path": "/points/-", "value": { "x": 0, "y": 8 } }
            ]
            """));

        AssertSuccess(result);
        var saved = Assert.IsType<PolygonNode>(Assert.Single(Reload(path).Root.Children));
        Assert.Equal(4, saved.Points.Count);
        Assert.Equal(20, saved.Points[1].X);
        Assert.Equal(8, saved.Points[3].Y);
    }

    [Fact]
    public void Set_ArrayIndexOutOfRange_FailsWithIndex()
    {
        var (_, handle) = CreateDocument();
        var polygon = _engine.AddNode(handle.Document, null, new NewNodeSpec { Type = "polygon", Points = "0,0 10,0" });
        _engine.Save(handle);

        var result = _engine.ApplyPatch(handle, Ops($$"""
            [{ "op": "set", "nodeId": "{{polygon.Id}}", "path": "/points/5/x", "value": 1 }]
            """));

        Assert.False(result.Success);
        Assert.Equal(EngineErrors.InvalidArguments, result.Error!.Code);
        Assert.Equal(0, result.Error.OperationIndex);
        Assert.Contains("use '-' to append", result.Error.Message);
    }

    [Fact]
    public void Set_StyleFillRule_OnPolygon_IsSavedAndValidated()
    {
        var (path, handle) = CreateDocument();
        var polygon = _engine.AddNode(handle.Document, null, new NewNodeSpec { Type = "polygon", Points = "0,0 10,0 5,8" });
        _engine.Save(handle);

        AssertSuccess(_engine.ApplyPatch(handle, Ops($$"""
            [{ "op": "set", "nodeId": "{{polygon.Id}}", "path": "/style/fillRule", "value": "evenodd" }]
            """)));
        Assert.Equal(FillRules.EvenOdd, Assert.IsType<PolygonNode>(Assert.Single(Reload(path).Root.Children)).Style!.FillRule);

        var invalid = _engine.ApplyPatch(handle, Ops($$"""
            [
              { "op": "set", "nodeId": "{{polygon.Id}}", "path": "/style/fillRule", "value": "even-odd" }
            ]
            """));

        Assert.False(invalid.Success);
        Assert.Equal("STYLE012", invalid.Error!.Code);
        Assert.Equal(0, invalid.Error.OperationIndex);
        Assert.Equal("root.children[0].style.fillRule", invalid.Error.Path);
        Assert.Equal(FillRules.EvenOdd, Assert.IsType<PolygonNode>(Assert.Single(Reload(path).Root.Children)).Style!.FillRule);
    }

    [Theory]
    [InlineData("/id")]
    [InlineData("/type")]
    public void Set_OnIdOrType_IsRejected(string pointer)
    {
        var (path, handle, rect) = CreateDocumentWithRectangle();
        var before = File.ReadAllBytes(path);

        var result = _engine.ApplyPatch(handle, Ops($$"""
            [{ "op": "set", "nodeId": "{{rect}}", "path": "{{pointer}}", "value": "ellipse" }]
            """));

        Assert.False(result.Success);
        Assert.Equal(EngineErrors.InvalidProperty, result.Error!.Code);
        Assert.Equal(0, result.Error.OperationIndex);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData("set", "/children")]
    [InlineData("set", "/children/0/type")]
    [InlineData("set", "/children/0/id")]
    [InlineData("set", "/children/0/src")]
    [InlineData("set", "/children/-")]
    [InlineData("unset", "/children")]
    [InlineData("unset", "/children/0")]
    public void SetOrUnset_ThroughChildren_IsRejected(string op, string pointer)
    {
        var (path, handle, _) = CreateDocumentWithRectangle();
        var before = File.ReadAllBytes(path);
        var value = op == "set" ? """, "value": { "type": "image", "src": "../outside.png", "width": 1, "height": 1 }""" : "";

        var result = _engine.ApplyPatch(handle, Ops($$"""
            [{ "op": "{{op}}", "nodeId": "{{handle.Document.Root.Id}}", "path": "{{pointer}}"{{value}} }]
            """));

        Assert.False(result.Success);
        Assert.Equal(EngineErrors.InvalidProperty, result.Error!.Code);
        Assert.Equal(0, result.Error.OperationIndex);
        Assert.Contains("children", result.Error.Message);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Single(handle.Document.Root.Children);
    }

    [Fact]
    public void Set_WrongValueType_FailsWithIndex()
    {
        var (_, handle, rect) = CreateDocumentWithRectangle();

        var result = _engine.ApplyPatch(handle, Ops($$"""
            [{ "op": "set", "nodeId": "{{rect}}", "path": "/width", "value": "wide" }]
            """));

        Assert.False(result.Success);
        Assert.Equal(EngineErrors.InvalidProperty, result.Error!.Code);
        Assert.Equal(0, result.Error.OperationIndex);
    }

    [Fact]
    public void Set_OnRootFrame_Works()
    {
        var (path, handle) = CreateDocument();

        var result = _engine.ApplyPatch(handle, Ops($$"""
            [{ "op": "set", "nodeId": "{{handle.Document.Root.Id}}", "path": "/clipContent", "value": false }]
            """));

        AssertSuccess(result);
        Assert.False(Reload(path).Root.ClipContent);
    }

    [Fact]
    public void Unset_RevertsPropertyToDefault()
    {
        var (path, handle, rect) = CreateDocumentWithRectangle(fill: "#123456");

        var result = _engine.ApplyPatch(handle, Ops($$"""
            [
              { "op": "unset", "nodeId": "{{rect}}", "path": "/style/fill" },
              { "op": "unset", "nodeId": "{{rect}}", "path": "/name" }
            ]
            """));

        AssertSuccess(result);
        var saved = Assert.IsType<RectangleNode>(Assert.Single(Reload(path).Root.Children));
        Assert.Null(saved.Style?.Fill);
        Assert.Null(saved.Name);
    }

    [Fact]
    public void Unset_MissingPath_FailsWithIndex()
    {
        var (_, handle, rect) = CreateDocumentWithRectangle();

        var result = _engine.ApplyPatch(handle, Ops($$"""
            [{ "op": "unset", "nodeId": "{{rect}}", "path": "/style/stroke/dashArray" }]
            """));

        Assert.False(result.Success);
        Assert.Equal(EngineErrors.InvalidProperty, result.Error!.Code);
        Assert.Equal(0, result.Error.OperationIndex);
        Assert.Contains("does not exist", result.Error.Message);
    }

    [Fact]
    public void SetNode_ReplacesNodeAndChildrenInPlace()
    {
        var (path, handle) = CreateDocument();
        var group = _engine.AddNode(handle.Document, null, new NewNodeSpec { Type = "group", Name = "g" });
        _engine.AddNode(handle.Document, group.Id, new NewNodeSpec { Type = "rectangle" });
        _engine.AddNode(handle.Document, group.Id, new NewNodeSpec { Type = "rectangle" });
        var after = _engine.AddNode(handle.Document, null, new NewNodeSpec { Type = "ellipse", Name = "after" });
        _engine.Save(handle);

        var result = _engine.ApplyPatch(handle, Ops($$"""
            [{ "op": "setNode", "nodeId": "{{group.Id}}",
               "node": { "id": "{{group.Id}}", "type": "group", "name": "replaced",
                         "children": [ { "type": "text", "text": "only child" } ] } }]
            """));

        AssertSuccess(result);
        var root = Reload(path).Root;
        Assert.Equal(2, root.Children.Count);
        var replaced = Assert.IsType<GroupNode>(root.Children[0]);
        Assert.Equal("replaced", replaced.Name);
        Assert.Equal(group.Order, replaced.Order);
        var child = Assert.IsType<TextNode>(Assert.Single(replaced.Children));
        Assert.Equal("only child", child.Text);
        Assert.NotEqual(Guid.Empty, child.Id);
        Assert.Equal(after.Id, root.Children[1].Id);
    }

    [Fact]
    public void SetNode_IdMismatch_FailsWithIndex()
    {
        var (_, handle, rect) = CreateDocumentWithRectangle();

        var result = _engine.ApplyPatch(handle, Ops($$"""
            [{ "op": "setNode", "nodeId": "{{rect}}", "node": { "id": "{{Guid.NewGuid()}}", "type": "rectangle" } }]
            """));

        Assert.False(result.Success);
        Assert.Equal(EngineErrors.InvalidProperty, result.Error!.Code);
        Assert.Equal(0, result.Error.OperationIndex);
    }

    [Fact]
    public void SetNode_ContainerWithoutChildrenArray_IsRejected()
    {
        var (_, handle) = CreateDocument();
        var group = _engine.AddNode(handle.Document, null, new NewNodeSpec { Type = "group" });
        _engine.Save(handle);

        var result = _engine.ApplyPatch(handle, Ops($$"""
            [{ "op": "setNode", "nodeId": "{{group.Id}}", "node": { "id": "{{group.Id}}", "type": "group" } }]
            """));

        Assert.False(result.Success);
        Assert.Contains("children", result.Error!.Message);
    }

    [Fact]
    public void AddNode_ReturnsIdAndAssignsDefaultOrder()
    {
        var (path, handle) = CreateDocument();

        var result = _engine.ApplyPatch(handle, Ops("""
            [
              { "op": "addNode", "node": { "type": "rectangle", "width": 10, "height": 10 } },
              { "op": "addNode", "node": { "type": "ellipse", "width": 10, "height": 10 } }
            ]
            """));

        AssertSuccess(result);
        Assert.Equal(2, result.CreatedIds.Count);
        var root = Reload(path).Root;
        Assert.Equal(result.CreatedIds[0], root.Children[0].Id);
        Assert.Equal(result.CreatedIds[1], root.Children[1].Id);
        Assert.Equal("a0", root.Children[0].Order);
        Assert.Equal("a1", root.Children[1].Order);
    }

    [Fact]
    public void AddNode_AfterId_PlacesNodeBetweenSiblings()
    {
        var (path, handle) = CreateDocument();
        var first = _engine.AddNode(handle.Document, null, new NewNodeSpec { Type = "rectangle", Name = "first" });
        _engine.AddNode(handle.Document, null, new NewNodeSpec { Type = "rectangle", Name = "last" });
        _engine.Save(handle);

        var result = _engine.ApplyPatch(handle, Ops($$"""
            [{ "op": "addNode", "afterId": "{{first.Id}}", "node": { "type": "text", "text": "middle" } }]
            """));

        AssertSuccess(result);
        var names = _engine.ListNodes(Reload(path)).Skip(1).Select(node => node.Name ?? node.Type).ToArray();
        Assert.Equal(["first", "text", "last"], names);
    }

    [Fact]
    public void AddNode_NestedSubtreeGetsIdsAndSequentialOrders()
    {
        var (path, handle) = CreateDocument();

        var result = _engine.ApplyPatch(handle, Ops("""
            [{ "op": "addNode", "node": { "type": "group", "name": "g", "children": [
                 { "type": "rectangle" }, { "type": "ellipse" }, { "type": "text", "text": "t" } ] } }]
            """));

        AssertSuccess(result);
        var group = Assert.IsType<GroupNode>(Assert.Single(Reload(path).Root.Children));
        Assert.Equal(["a0", "a1", "a2"], group.Children.Select(child => child.Order));
        Assert.Equal(3, group.Children.Select(child => child.Id).Distinct().Count());
    }

    [Fact]
    public void AddNode_IntoNonContainer_FailsWithIndex()
    {
        var (_, handle, rect) = CreateDocumentWithRectangle();

        var result = _engine.ApplyPatch(handle, Ops($$"""
            [{ "op": "addNode", "parentId": "{{rect}}", "node": { "type": "ellipse" } }]
            """));

        Assert.False(result.Success);
        Assert.Equal(EngineErrors.ParentNotContainer, result.Error!.Code);
        Assert.Equal(0, result.Error.OperationIndex);
    }

    [Fact]
    public void AddNode_UnknownType_IsRejected()
    {
        var (_, handle) = CreateDocument();

        var result = _engine.ApplyPatch(handle, Ops("""
            [{ "op": "addNode", "node": { "type": "hologram" } }]
            """));

        Assert.False(result.Success);
        Assert.Equal(EngineErrors.InvalidNodeType, result.Error!.Code);
    }

    [Fact]
    public void DeleteMoveAndReorder_ReuseEngineSemantics()
    {
        var (path, handle) = CreateDocument();
        var group = _engine.AddNode(handle.Document, null, new NewNodeSpec { Type = "group", Name = "group" });
        var a = _engine.AddNode(handle.Document, null, new NewNodeSpec { Type = "rectangle", Name = "a" });
        var b = _engine.AddNode(handle.Document, null, new NewNodeSpec { Type = "rectangle", Name = "b" });
        var c = _engine.AddNode(handle.Document, null, new NewNodeSpec { Type = "rectangle", Name = "c" });
        var d = _engine.AddNode(handle.Document, null, new NewNodeSpec { Type = "rectangle", Name = "d" });
        _engine.Save(handle);

        var result = _engine.ApplyPatch(handle, Ops($$"""
            [
              { "op": "deleteNode", "nodeId": "{{d.Id}}" },
              { "op": "moveNode", "nodeId": "{{a.Id}}", "parentId": "{{group.Id}}" },
              { "op": "moveNode", "nodeId": "{{b.Id}}", "parentId": "{{group.Id}}", "afterId": "{{a.Id}}" },
              { "op": "reorderNode", "nodeId": "{{b.Id}}", "afterId": null },
              { "op": "reorderNode", "nodeId": "{{group.Id}}", "afterId": "{{c.Id}}" }
            ]
            """));

        AssertSuccess(result);
        var order = _engine.ListNodes(Reload(path)).Skip(1).Select(node => $"{node.Depth}:{node.Name}").ToArray();
        Assert.Equal(["1:c", "1:group", "2:b", "2:a"], order);
    }

    [Fact]
    public void DeleteRoot_FailsWithIndexAndLeavesFileUntouched()
    {
        var (path, handle) = CreateDocument();
        var before = File.ReadAllBytes(path);

        var result = _engine.ApplyPatch(handle, Ops($$"""
            [{ "op": "deleteNode", "nodeId": "{{handle.Document.Root.Id}}" }]
            """));

        Assert.False(result.Success);
        Assert.Equal(EngineErrors.CannotDeleteRoot, result.Error!.Code);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void ColorTokens_SetAndRemove()
    {
        var (path, handle) = CreateDocument();
        _engine.SetColorToken(handle.Document, "old", "#000000");
        _engine.Save(handle);

        var result = _engine.ApplyPatch(handle, Ops("""
            [
              { "op": "setColorToken", "name": "primary", "value": "#336699" },
              { "op": "removeColorToken", "name": "old" }
            ]
            """));

        AssertSuccess(result);
        var tokens = Reload(path).Tokens!.Colors!;
        Assert.Equal("#336699", tokens["primary"]);
        Assert.DoesNotContain("old", tokens.Keys);
    }

    [Fact]
    public void RemoveColorToken_Missing_FailsWithIndex()
    {
        var (_, handle) = CreateDocument();

        var result = _engine.ApplyPatch(handle, Ops("""
            [{ "op": "removeColorToken", "name": "ghost" }]
            """));

        Assert.False(result.Success);
        Assert.Equal("TOKEN_NOT_FOUND", result.Error!.Code);
        Assert.Equal(0, result.Error.OperationIndex);
    }

    [Fact]
    public void SetCanvas_UpdatesDimensionsAndClearsBackground()
    {
        var (path, handle) = CreateDocument(background: "#ffffff");

        var result = _engine.ApplyPatch(handle, Ops("""
            [
              { "op": "setCanvas", "width": 640, "height": 480 },
              { "op": "setCanvas", "background": null }
            ]
            """));

        AssertSuccess(result);
        var canvas = Reload(path).Canvas;
        Assert.Equal(640, canvas.Width);
        Assert.Equal(480, canvas.Height);
        Assert.Null(canvas.Background);
    }

    [Theory]
    [InlineData("""{ "op": "setCanvas" }""", "INVALID_ARGUMENTS")]
    [InlineData("""{ "op": "setCanvas", "width": 0 }""", "INVALID_DIMENSIONS")]
    [InlineData("""{ "op": "setCanvas", "background": "blue" }""", "INVALID_PROPERTY")]
    [InlineData("""{ "op": "setCanvas", "background": 12 }""", "INVALID_ARGUMENTS")]
    public void SetCanvas_RejectsInvalidInput(string operation, string code)
    {
        var (_, handle) = CreateDocument();

        var result = _engine.ApplyPatch(handle, Ops($"[{operation}]"));

        Assert.False(result.Success);
        Assert.Equal(code, result.Error!.Code);
        Assert.Equal(0, result.Error.OperationIndex);
    }

    // ---- Atomicity, revisions, validation ----

    [Fact]
    public void Patch_ThirdOperationInvalid_LeavesFileBytesRevisionAndMemoryUntouched()
    {
        var (path, handle, rect) = CreateDocumentWithRectangle();
        var before = File.ReadAllBytes(path);
        var revn = handle.Document.Revn;

        var result = _engine.ApplyPatch(handle, Ops($$"""
            [
              { "op": "set", "nodeId": "{{rect}}", "path": "/name", "value": "renamed" },
              { "op": "addNode", "node": { "type": "ellipse" } },
              { "op": "deleteNode", "nodeId": "{{Guid.NewGuid()}}" }
            ]
            """));

        Assert.False(result.Success);
        Assert.Equal(EngineErrors.NodeNotFound, result.Error!.Code);
        Assert.Equal(2, result.Error.OperationIndex);
        Assert.StartsWith("Operation 2 (deleteNode):", result.Error.Message);
        Assert.Equal(0, result.Applied);
        Assert.Empty(result.CreatedIds);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(revn, handle.Document.Revn);
        Assert.Equal(revn, Reload(path).Revn);
        Assert.Single(handle.Document.Root.Children);
        Assert.Equal("rect", handle.Document.Root.Children[0].Name);
    }

    [Fact]
    public void Patch_IncrementsRevisionOnceForManyOperations()
    {
        var (path, handle, rect) = CreateDocumentWithRectangle();
        var revn = handle.Document.Revn;

        var result = _engine.ApplyPatch(handle, Ops($$"""
            [
              { "op": "set", "nodeId": "{{rect}}", "path": "/name", "value": "one" },
              { "op": "addNode", "node": { "type": "ellipse" } },
              { "op": "setColorToken", "name": "p", "value": "#000000" },
              { "op": "setCanvas", "width": 300 }
            ]
            """));

        AssertSuccess(result);
        Assert.Equal(revn + 1, result.Revn);
        Assert.Equal(revn + 1, handle.Document.Revn);
        Assert.Equal(revn + 1, Reload(path).Revn);
        Assert.Equal("one", handle.Document.Root.Children[0].Name);
        Assert.Equal(300, handle.Document.Canvas.Width);
    }

    [Fact]
    public void Patch_ExpectedRevisionMismatch_ReturnsRevisionConflictWithoutWriting()
    {
        var (path, handle, rect) = CreateDocumentWithRectangle();
        var before = File.ReadAllBytes(path);

        var result = _engine.ApplyPatch(handle, Ops($$"""
            [{ "op": "set", "nodeId": "{{rect}}", "path": "/name", "value": "x" }]
            """), expectedRevn: handle.Document.Revn + 7);

        Assert.False(result.Success);
        Assert.Equal(EngineErrors.RevisionConflict, result.Error!.Code);
        Assert.Null(result.Error.OperationIndex);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal("rect", handle.Document.Root.Children[0].Name);
    }

    [Fact]
    public void Patch_ExpectedRevisionMatch_Succeeds()
    {
        var (path, handle, rect) = CreateDocumentWithRectangle();

        var result = _engine.ApplyPatch(handle, Ops($$"""
            [{ "op": "set", "nodeId": "{{rect}}", "path": "/name", "value": "x" }]
            """), expectedRevn: handle.Document.Revn);

        AssertSuccess(result);
        Assert.Equal("x", Reload(path).Root.Children[0].Name);
    }

    [Fact]
    public void Patch_ValidationError_IsAttributedToOperationIndex()
    {
        var (path, handle, rect) = CreateDocumentWithRectangle();
        var before = File.ReadAllBytes(path);

        var result = _engine.ApplyPatch(handle, Ops($$"""
            [
              { "op": "set", "nodeId": "{{rect}}", "path": "/name", "value": "fine" },
              { "op": "set", "nodeId": "{{rect}}", "path": "/width", "value": -5 },
              { "op": "set", "nodeId": "{{rect}}", "path": "/height", "value": 7 }
            ]
            """));

        Assert.False(result.Success);
        Assert.Equal("RECT001", result.Error!.Code);
        Assert.Equal(1, result.Error.OperationIndex);
        Assert.Equal("root.children[0].width", result.Error.Path);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void Patch_ReturnsValidationWarnings()
    {
        var (_, handle) = CreateDocument();
        var pathNode = _engine.AddNode(handle.Document, null, new NewNodeSpec { Type = "path", PathData = "M0,0L1,1" });
        _engine.Save(handle);

        var result = _engine.ApplyPatch(handle, Ops($$"""
            [{ "op": "set", "nodeId": "{{pathNode.Id}}", "path": "/d", "value": "" }]
            """));

        AssertSuccess(result);
        Assert.Contains(result.Warnings, warning => warning.Code == "PATH001");
    }

    [Fact]
    public void Patch_PreexistingValidationError_FailsWithoutIndexUnlessResolved()
    {
        var (path, handle, rect) = CreateDocumentWithRectangle();
        var document = Reload(path);
        ((RectangleNode)document.Root.Children[0]).Width = -1;
        File.WriteAllText(path, DocumentSerializer.Serialize(document));
        var broken = _engine.Open(path)!;

        var unresolved = _engine.ApplyPatch(broken, Ops($$"""
            [{ "op": "set", "nodeId": "{{rect}}", "path": "/name", "value": "still broken" }]
            """));
        Assert.False(unresolved.Success);
        Assert.Equal("RECT001", unresolved.Error!.Code);
        Assert.Null(unresolved.Error.OperationIndex);

        var resolved = _engine.ApplyPatch(broken, Ops($$"""
            [{ "op": "set", "nodeId": "{{rect}}", "path": "/width", "value": 12 }]
            """));
        AssertSuccess(resolved);
        Assert.Equal(12, ((RectangleNode)Reload(path).Root.Children[0]).Width);
    }

    [Fact]
    public void Patch_DuplicateIdFromAddNode_IsStructuralFailureWithIndex()
    {
        var (path, handle, rect) = CreateDocumentWithRectangle();
        var before = File.ReadAllBytes(path);

        var result = _engine.ApplyPatch(handle, Ops($$"""
            [{ "op": "addNode", "node": { "id": "{{rect}}", "type": "ellipse" } }]
            """));

        Assert.False(result.Success);
        Assert.Equal(EngineErrors.InvalidProperty, result.Error!.Code);
        Assert.Equal(0, result.Error.OperationIndex);
        Assert.Contains("Duplicate node ID", result.Error.Message);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void Patch_OperationCapAndEmptyList_AreRejectedByEngine()
    {
        var (_, handle) = CreateDocument();
        var tooMany = Enumerable.Range(0, PatchLimits.MaxOperations + 1)
            .Select(_ => (PatchOperation)new SetColorTokenOperation("p", "#000000"))
            .ToArray();

        var capped = _engine.ApplyPatch(handle, tooMany);
        var empty = _engine.ApplyPatch(handle, []);

        Assert.Equal(EngineErrors.ResourceLimit, capped.Error!.Code);
        Assert.Equal(EngineErrors.InvalidArguments, empty.Error!.Code);
    }

    [Fact]
    public void Patch_MaximumOperationCount_IsAccepted()
    {
        var (path, handle) = CreateDocument();
        var operations = Enumerable.Range(0, PatchLimits.MaxOperations)
            .Select(i => (PatchOperation)new SetColorTokenOperation($"c{i}", "#000000"))
            .ToArray();

        var result = _engine.ApplyPatch(handle, operations);

        AssertSuccess(result);
        Assert.Equal(PatchLimits.MaxOperations, Reload(path).Tokens!.Colors!.Count);
    }

    [Fact]
    public void Patch_StoreBackedHandle_SavesThroughStoreWithRevisionCheck()
    {
        var store = new InMemoryStore();
        var engine = new DocumentEngine(store);
        var document = engine.CreateDocument("stored", 100, 100);
        engine.SaveDocument(document);
        var handle = engine.Open(document.Id.ToString())!;

        var conflict = engine.ApplyPatch(handle, Ops("""[{ "op": "setCanvas", "width": 50 }]"""), expectedRevn: 42);
        var success = engine.ApplyPatch(handle, Ops("""[{ "op": "setCanvas", "width": 50 }]"""), expectedRevn: 1);

        Assert.Equal(EngineErrors.RevisionConflict, conflict.Error!.Code);
        AssertSuccess(success);
        Assert.Equal(2, engine.LoadDocument(document.Id)!.Revn);
        Assert.Equal(50, engine.LoadDocument(document.Id)!.Canvas.Width);
    }

    [Fact]
    public void Set_OnUnknownNode_PreservesOriginalDiscriminator()
    {
        var (path, handle) = CreateDocument();
        var unknownId = Guid.NewGuid();
        var json = DocumentSerializer.Serialize(handle.Document).Replace(
            "\"children\": []",
            $$"""
            "children": [ { "type": "hologram", "id": "{{unknownId}}", "order": "a0", "name": "h", "beam": 3 } ]
            """);
        File.WriteAllText(path, json);
        var opened = _engine.Open(path)!;

        var result = _engine.ApplyPatch(opened, Ops($$"""
            [{ "op": "set", "nodeId": "{{unknownId}}", "path": "/name", "value": "renamed" }]
            """));

        AssertSuccess(result);
        var saved = File.ReadAllText(path);
        Assert.Contains("\"type\": \"hologram\"", saved);
        Assert.Contains("\"beam\": 3", saved);
        Assert.Contains("\"name\": \"renamed\"", saved);
    }

    // ---- Helpers ----

    private static IReadOnlyList<PatchOperation> Ops(string json)
    {
        var parsed = PatchOperationParser.Parse(json);
        Assert.True(parsed.Success, parsed.Error?.Message);
        return parsed.Operations!;
    }

    private static void AssertSuccess(PatchResult result) =>
        Assert.True(result.Success, result.Error is null ? null : $"{result.Error.Code}: {result.Error.Message}");

    private (string Path, DocumentHandle Handle) CreateDocument(string? background = null)
    {
        var path = Path.Combine(_testDir, $"{Guid.NewGuid():N}.agd.json");
        var document = _engine.CreateDocument("patch-test", 200, 100, background);
        _engine.CreateDocumentFile(document, path);
        return (path, _engine.Open(path)!);
    }

    private (string Path, DocumentHandle Handle, Guid RectangleId) CreateDocumentWithRectangle(string? fill = null)
    {
        var (path, handle) = CreateDocument();
        var rect = _engine.AddNode(handle.Document, null, new NewNodeSpec
        {
            Type = "rectangle",
            Name = "rect",
            Width = 40,
            Height = 20,
            Fill = fill
        });
        _engine.Save(handle);
        return (path, handle, rect.Id);
    }

    private Document Reload(string path) => _engine.LoadDocumentFromFile(path)!;

    private sealed class InMemoryStore : IDocumentStore
    {
        private readonly Dictionary<Guid, string> _documents = [];

        public DocumentStoreEntry? Load(Guid id) =>
            _documents.TryGetValue(id, out var json)
                ? new DocumentStoreEntry(DocumentSerializer.Deserialize(json)!, SourcePath: null)
                : null;

        public void Save(DocumentStoreEntry entry, long? expectedRevn = null)
        {
            if (expectedRevn.HasValue && Load(entry.Document.Id)?.Document is { } existing &&
                existing.Revn != expectedRevn.Value)
            {
                throw new DocumentEngineException(EngineErrors.RevisionConflict,
                    $"Revision conflict: expected {expectedRevn.Value}, got {existing.Revn}");
            }

            _documents[entry.Document.Id] = DocumentSerializer.Serialize(entry.Document);
        }

        public void Create(Document document) => _documents.Add(document.Id, DocumentSerializer.Serialize(document));

        public void Delete(Guid id) => _documents.Remove(id);

        public IEnumerable<DocumentInfo> List() =>
            _documents.Keys.Select(id => new DocumentInfo(id, Load(id)!.Document.Name, DateTimeOffset.UnixEpoch));
    }
}
