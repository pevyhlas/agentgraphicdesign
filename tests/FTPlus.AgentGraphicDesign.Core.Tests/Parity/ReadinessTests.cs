using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Export;
using FTPlus.AgentGraphicDesign.Core.PathOperations;
using FTPlus.AgentGraphicDesign.Core.Persistence;
using FTPlus.AgentGraphicDesign.Core.Rendering;
using FTPlus.AgentGraphicDesign.Skia;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Parity;

/// <summary>
/// Readiness tests that exercise the full v0.1 MVP workflow.
/// These tests verify that all operations work end-to-end through the DocumentEngine,
/// which is the shared API used by both CLI and MCP surfaces.
/// </summary>
public class ReadinessTests : IDisposable
{
    private readonly string _tempDir;
    private readonly DocumentEngine _engine;

    public ReadinessTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"agd-readiness-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var store = new FileDocumentStore(_tempDir);
        var renderer = new SkiaRenderer();
        _engine = new DocumentEngine(store, [new SvgExportAdapter()], renderer);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void Workflow_CreateAndValidate()
    {
        var doc = _engine.CreateDocument("test-design", 800, 600, "#ffffff");

        Assert.NotEqual(Guid.Empty, doc.Id);
        Assert.Equal("test-design", doc.Name);
        Assert.Equal(800, doc.Canvas.Width);
        Assert.Equal(600, doc.Canvas.Height);

        var result = _engine.Validate(doc);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Workflow_AddAllNodeTypes()
    {
        var doc = _engine.CreateDocument("shapes", 400, 400);

        // Rectangle
        var rect = _engine.AddNode(doc, null, new NewNodeSpec
        {
            Type = "rectangle",
            Name = "rect1",
            X = 10,
            Y = 10,
            Width = 80,
            Height = 60,
            Fill = "#ff0000"
        });
        Assert.IsType<RectangleNode>(rect);

        // Ellipse
        var ellipse = _engine.AddNode(doc, null, new NewNodeSpec
        {
            Type = "ellipse",
            Name = "circle1",
            X = 100,
            Y = 10,
            Width = 60,
            Height = 60,
            Fill = "#00ff00"
        });
        Assert.IsType<EllipseNode>(ellipse);

        // Text
        var text = _engine.AddNode(doc, null, new NewNodeSpec
        {
            Type = "text",
            Name = "label1",
            X = 10,
            Y = 100,
            Text = "Hello World",
            Fill = "#000000"
        });
        Assert.IsType<TextNode>(text);

        // Path
        var path = _engine.AddNode(doc, null, new NewNodeSpec
        {
            Type = "path",
            Name = "triangle",
            PathData = "M 0 0 L 50 100 L 100 0 Z",
            Fill = "#0000ff"
        });
        Assert.IsType<PathNode>(path);

        // Line
        var line = _engine.AddNode(doc, null, new NewNodeSpec
        {
            Type = "line",
            Name = "line1",
            X = 200,
            Y = 10,
            X2 = 300,
            Y2 = 100,
            Stroke = "#333333"
        });
        Assert.IsType<LineNode>(line);

        // Group
        var group = _engine.AddNode(doc, null, new NewNodeSpec
        {
            Type = "group",
            Name = "group1"
        });
        Assert.IsType<GroupNode>(group);

        // Frame (nested)
        var frame = _engine.AddNode(doc, null, new NewNodeSpec
        {
            Type = "frame",
            Name = "frame1",
            X = 250,
            Y = 150,
            Width = 100,
            Height = 100
        });
        Assert.IsType<FrameNode>(frame);

        Assert.Equal(8, _engine.CountNodes(doc)); // root + 7 children
    }

    [Fact]
    public void Workflow_UpdateAndMove()
    {
        var doc = _engine.CreateDocument("move-test", 400, 400);

        var rect = _engine.AddNode(doc, null, new NewNodeSpec
        {
            Type = "rectangle",
            Name = "movable",
            X = 0,
            Y = 0,
            Width = 50,
            Height = 50,
            Fill = "#ff0000"
        });

        var group = _engine.AddNode(doc, null, new NewNodeSpec
        {
            Type = "group",
            Name = "container"
        });

        // Update position
        _engine.UpdateNode(doc, rect.Id, new NodeUpdates { X = 100, Y = 100 });
        var updated = _engine.FindNode(doc, rect.Id) as RectangleNode;
        Assert.Equal(100, updated!.Transform!.X);

        // Move to group
        _engine.MoveNode(doc, rect.Id, group.Id);
        var groupNode = _engine.FindNode(doc, group.Id) as GroupNode;
        Assert.Single(groupNode!.Children);
    }

    [Fact]
    public void Workflow_DeleteAndReorder()
    {
        var doc = _engine.CreateDocument("reorder-test", 400, 400);

        var r1 = _engine.AddNode(doc, null, new NewNodeSpec
        {
            Type = "rectangle",
            Name = "r1",
            X = 0,
            Y = 0,
            Width = 50,
            Height = 50
        });
        var r2 = _engine.AddNode(doc, null, new NewNodeSpec
        {
            Type = "rectangle",
            Name = "r2",
            X = 60,
            Y = 0,
            Width = 50,
            Height = 50
        });
        var r3 = _engine.AddNode(doc, null, new NewNodeSpec
        {
            Type = "rectangle",
            Name = "r3",
            X = 120,
            Y = 0,
            Width = 50,
            Height = 50
        });

        Assert.Equal(4, _engine.CountNodes(doc));

        // Reorder r3 before r1
        _engine.ReorderNode(doc, r3.Id, r1.Id, null);

        // Delete r2
        _engine.RemoveNode(doc, r2.Id);
        Assert.Equal(3, _engine.CountNodes(doc));
    }

    [Fact]
    public async Task Workflow_ExportSvg()
    {
        var doc = _engine.CreateDocument("export-test", 200, 200);
        _engine.AddNode(doc, null, new NewNodeSpec
        {
            Type = "rectangle",
            X = 10,
            Y = 10,
            Width = 80,
            Height = 80,
            Fill = "#ff0000"
        });

        var result = await _engine.ExportAsync(doc, new ExportOptions
        {
            Format = "svg"
        });

        Assert.NotNull(result.Data);
        Assert.True(result.Data.Length > 0);
        var content = System.Text.Encoding.UTF8.GetString(result.Data);
        Assert.Contains("<svg", content);
        Assert.Contains("rect", content);
    }

    [Fact]
    public void Workflow_RenderPng()
    {
        var doc = _engine.CreateDocument("render-test", 100, 100);
        _engine.AddNode(doc, null, new NewNodeSpec
        {
            Type = "ellipse",
            X = 25,
            Y = 25,
            Width = 50,
            Height = 50,
            Fill = "#00ff00"
        });

        var result = _engine.Render(doc, new RenderOptions
        {
            Format = RenderFormat.Png,
            Scale = 1.0
        });

        Assert.NotNull(result.Data);
        Assert.True(result.Data.Length > 0);
        // PNG magic bytes
        Assert.Equal(0x89, result.Data[0]);
        Assert.Equal(0x50, result.Data[1]); // P
        Assert.Equal(0x4E, result.Data[2]); // N
        Assert.Equal(0x47, result.Data[3]); // G
    }

    [Fact]
    public void Workflow_ImportSvg()
    {
        var svgContent = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100">
                <rect x="10" y="10" width="30" height="30" fill="#ff0000"/>
                <circle cx="70" cy="30" r="20" fill="#00ff00"/>
            </svg>
            """;

        var result = _engine.ImportSvg(svgContent);

        Assert.True(result.Success);
        Assert.NotNull(result.Document);
        Assert.True(_engine.CountNodes(result.Document) >= 3); // root + rect + ellipse
    }

    [Fact]
    public void Workflow_TokensAndPalettes()
    {
        var doc = _engine.CreateDocument("tokens-test", 200, 200);

        // Set color tokens
        _engine.SetColorToken(doc, "primary", "#0066cc");
        _engine.SetColorToken(doc, "secondary", "#ff6600");

        // List tokens
        var colors = _engine.GetColorTokens(doc);
        Assert.Equal(2, colors.Count);

        // Apply palette
        var paletteResult = _engine.ApplyPalette(doc, "tailwind");
        Assert.True(paletteResult.Success);
        Assert.True(paletteResult.ColorCount > 100);

        // List palettes
        var palettes = _engine.ListPalettes();
        Assert.Equal(3, palettes.Count);

        // Export DTCG
        var exportResult = _engine.ExportDtcgTokens(doc);
        Assert.True(exportResult.Success);
        Assert.Contains("$value", exportResult.Content!);
    }

    [Fact]
    public void Workflow_PathOperations()
    {
        // Boolean union
        var unionResult = _engine.PathBooleanOp(
            "M0,0 L100,0 L100,100 L0,100 Z",
            "M50,50 L150,50 L150,150 L50,150 Z",
            BooleanOperation.Union
        );
        Assert.True(unionResult.Success);
        Assert.False(string.IsNullOrEmpty(unionResult.PathData));

        // Boolean difference
        var diffResult = _engine.PathBooleanOp(
            "M0,0 L100,0 L100,100 L0,100 Z",
            "M25,25 L75,25 L75,75 L25,75 Z",
            BooleanOperation.Difference
        );
        Assert.True(diffResult.Success);

        // Stroke offset
        var offsetResult = _engine.PathStrokeOffset(
            "M0,0 L100,0 L100,100 L0,100 Z",
            5.0
        );
        Assert.True(offsetResult.Success);

        // Union all
        var unionAllResult = _engine.PathUnionAll([
            "M0,0 L50,0 L50,50 L0,50 Z",
            "M25,25 L75,25 L75,75 L25,75 Z"
        ]);
        Assert.True(unionAllResult.Success);
    }

    [Fact]
    public void Workflow_SaveAndLoad()
    {
        var doc = _engine.CreateDocument("persist-test", 300, 300);
        _engine.AddNode(doc, null, new NewNodeSpec
        {
            Type = "text",
            Name = "saved-text",
            X = 50,
            Y = 50,
            Text = "Persisted!"
        });

        var filePath = Path.Combine(_tempDir, "test.agd.json");
        _engine.SaveDocumentToFile(doc, filePath);

        Assert.True(File.Exists(filePath));

        var loaded = _engine.LoadDocumentFromFile(filePath);
        Assert.NotNull(loaded);
        Assert.Equal(doc.Id, loaded.Id);
        Assert.Equal("persist-test", loaded.Name);
        Assert.Equal(2, _engine.CountNodes(loaded));
    }

    [Fact]
    public void Workflow_Validation()
    {
        var doc = _engine.CreateDocument("valid-doc", 400, 400);
        _engine.AddNode(doc, null, new NewNodeSpec
        {
            Type = "rectangle",
            X = 10,
            Y = 10,
            Width = 100,
            Height = 100,
            Fill = "#ff0000"
        });

        var result = _engine.Validate(doc);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task Workflow_FullMvpPipeline()
    {
        // This test exercises the complete MVP pipeline:
        // Create → Add nodes → Apply tokens → Export SVG → Export PNG

        // 1. Create document
        var doc = _engine.CreateDocument("mvp-pipeline", 400, 300, "#ffffff");

        // 2. Apply palette for brand colors
        _engine.ApplyPalette(doc, "tailwind");

        // 3. Add header rectangle using token reference
        _engine.AddNode(doc, null, new NewNodeSpec
        {
            Type = "rectangle",
            Name = "header",
            X = 0,
            Y = 0,
            Width = 400,
            Height = 80,
            Fill = "#3b82f6" // blue-500
        });

        // 4. Add title text
        _engine.AddNode(doc, null, new NewNodeSpec
        {
            Type = "text",
            Name = "title",
            X = 20,
            Y = 40,
            Text = "MVP Ready",
            Fill = "#ffffff"
        });

        // 5. Add content area
        _engine.AddNode(doc, null, new NewNodeSpec
        {
            Type = "rectangle",
            Name = "content",
            X = 20,
            Y = 100,
            Width = 360,
            Height = 180,
            Fill = "#f1f5f9", // slate-100
            Stroke = "#e2e8f0" // slate-200
        });

        // 6. Add icon circle
        _engine.AddNode(doc, null, new NewNodeSpec
        {
            Type = "ellipse",
            Name = "icon",
            X = 180,
            Y = 160,
            Width = 40,
            Height = 40,
            Fill = "#22c55e" // green-500
        });

        // 7. Validate
        var validation = _engine.Validate(doc);
        Assert.True(validation.IsValid);

        // 8. Export to SVG
        var svgResult = await _engine.ExportAsync(doc, new ExportOptions
        {
            Format = "svg"
        });
        Assert.NotNull(svgResult.Data);
        Assert.True(svgResult.Data.Length > 0);

        // 9. Render to PNG
        var renderResult = _engine.Render(doc, new RenderOptions
        {
            Format = RenderFormat.Png,
            Scale = 2.0
        });
        Assert.NotNull(renderResult.Data);
        Assert.True(renderResult.Data.Length > 1000); // Non-trivial PNG

        // 10. Save document
        var docPath = Path.Combine(_tempDir, "mvp-pipeline.agd.json");
        _engine.SaveDocumentToFile(doc, docPath);
        Assert.True(File.Exists(docPath));

        // 11. Export tokens
        var tokensResult = _engine.ExportDtcgTokens(doc);
        Assert.True(tokensResult.Success);
    }
}
