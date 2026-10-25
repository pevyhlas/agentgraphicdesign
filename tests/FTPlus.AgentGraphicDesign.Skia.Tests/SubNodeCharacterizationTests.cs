using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Export;
using FTPlus.AgentGraphicDesign.Core.Rendering;
using FTPlus.AgentGraphicDesign.Core.Serialization;
using SkiaSharp;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Skia.Tests;

/// <summary>T029: pre-builder bytes and independently derived D-10 geometry crop. Never regenerate to fix a refactor.</summary>
public sealed class SubNodeCharacterizationTests
{
    [Theory]
    [InlineData("nested", 24, 20)]
    [InlineData("token", 14, 10)]
    [InlineData("stroke", 20, 20)]
    public async Task IsolatedExportRetainsPreBuilderBytes(string name, int width, int height)
    {
        var (document, target) = Scene(name);
        var before = DocumentSerializer.Serialize(document);
        var engine = new DocumentEngine(renderer: new SkiaRenderer());
        var svg = await engine.ExportNodeAsync(document, target.Id, new ExportOptions());
        var png = engine.RenderNode(document, target.Id,
            new RenderOptions { SystemFonts = false }, CancellationToken.None);

        Assert.Equal((width, height), (png.Width, png.Height));
        Assert.Equal(before, DocumentSerializer.Serialize(document));
        var hash = Convert.ToHexStringLower(SHA256.HashData(png.Data));
        // Recording writes evidence only; the assertions below still run.
        if (Environment.GetEnvironmentVariable("AGD_RECORD_SUBNODE_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(Path.Combine(directory, name + ".svg"), svg.Data);
            await File.WriteAllTextAsync(Path.Combine(directory, name + ".sha256"), hash + "\n");
        }
        var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sub-node");
        Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(fixtures, name + ".svg")), svg.Data);
        if (OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64)
            Assert.Equal((await File.ReadAllTextAsync(Path.Combine(fixtures, name + ".sha256"))).Trim(), hash);

        using var bitmap = SKBitmap.Decode(png.Data);
        if (name == "nested")
        {
            // The child covers the clipped frame, then its own group opacity halves alpha.
            var pixel = bitmap.GetPixel(12, 10);
            Assert.Equal(128, pixel.Alpha);
            // Premultiply/unpremultiply quantization can lose one color unit at half alpha.
            Assert.InRange(pixel.Red, 0x23, 0x25);
            Assert.InRange(pixel.Green, 0x67, 0x69);
            Assert.InRange(pixel.Blue, 0xab, 0xad);
        }
        else if (name == "token")
            Assert.Equal(new SKColor(0x13, 0x57, 0x9b), bitmap.GetPixel(7, 5));
        else
        {
            // Centered four-unit stroke has actual ink [-2,-2,24,24], but D-10 retains
            // [0,0,20,20], clipping two units at each edge. Preserve, do not silently fix.
            Assert.Equal(SKColors.Black, bitmap.GetPixel(0, 10));
            Assert.Equal(SKColors.White, bitmap.GetPixel(10, 10));
        }
    }

    private static (Document, Node) Scene(string name)
    {
        var rectangle = new RectangleNode
        {
            Id = Guid.Parse("20000000-0000-0000-0000-000000000001"),
            Width = 20,
            Height = 20,
            Style = new Style { Fill = new Fill { Color = "#ffffff" }, Stroke = new Stroke { Color = "#000000", Width = 4 } }
        };
        Node target = rectangle;
        if (name == "token")
        {
            rectangle.Width = 14;
            rectangle.Height = 10;
            rectangle.Transform = new Transform { X = -3, Y = 7 };
            rectangle.Style = new Style { Fill = new Fill { TokenRef = "{ink}" } };
        }
        else if (name == "nested")
        {
            rectangle.Width = 100;
            rectangle.Height = 100;
            rectangle.Transform = new Transform { X = -20, Y = -20 };
            rectangle.Style = new Style { Fill = new Fill { Color = "#2468ac" } };
            target = new GroupNode
            {
                Id = Guid.Parse("20000000-0000-0000-0000-000000000002"),
                Transform = new Transform { X = 30, Y = 40, Rotation = 90, ScaleX = 2, ScaleY = 3 },
                Style = new Style { Opacity = 0.5 },
                Children = [new FrameNode
                {
                    Id = Guid.Parse("20000000-0000-0000-0000-000000000003"),
                    Width = 10, Height = 8, ClipContent = true,
                    Transform = new Transform { X = 5, Y = 2 }, Children = [rectangle]
                }]
            };
            // Frame [5,2,10,8], scaled 2×3, rotated 90°, translated (30,40):
            // world [0,50,24,20]. The enclosing ancestor below is intentionally ignored.
        }
        var document = new Document
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Name = "T029-" + name,
            Canvas = new Canvas { Width = 200, Height = 150, Background = "#ff00ff" },
            Root = new FrameNode
            {
                Width = 200,
                Height = 150,
                ClipContent = true,
                Transform = new Transform { X = 1000, Y = 2000, ScaleX = 4, ScaleY = 5 },
                Children = [target]
            }
        };
        new DocumentEngine().SetColorToken(document, "ink", "#13579b");
        return (document, target);
    }
}
