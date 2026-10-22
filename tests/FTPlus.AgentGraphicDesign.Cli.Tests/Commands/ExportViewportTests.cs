using System.Xml.Linq;
using SkiaSharp;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Cli.Tests.Commands;

public class ExportViewportTests : CommandTestBase
{
    [Theory]
    [InlineData(128)]
    [InlineData(64)]
    public void RasterSizing_ScalesGeometryButViewportOverridesCropOrPad(int height)
    {
        var path = Path.Combine(TestDir, "viewport.agd.json");
        File.WriteAllText(path, $$$$"""
            {"version":1,"id":"11111111-1111-1111-1111-111111111111","name":"viewport","revn":1,
             "canvas":{"width":128,"height":{{{{height}}}}},
             "root":{"type":"frame","id":"22222222-2222-2222-2222-222222222222","width":128,"height":{{{{height}}}},
               "children":[{"type":"rectangle","id":"33333333-3333-3333-3333-333333333333",
                 "width":32,"height":{{{{height}}}},"transform":{"x":96},"style":{"fill":{"color":"#ff0000"}}}]}}
            """);

        using var scaled = Export("scaled.png", "--scale", "0.25");
        Assert.Equal(32, scaled.Width);
        Assert.Equal(height / 4, scaled.Height);
        Assert.Equal(SKColors.Red, scaled.GetPixel(28, scaled.Height / 2));
        Assert.Equal(0, scaled.GetPixel(4, scaled.Height / 2).Alpha);

        using var cropped = Export("cropped.png", "--width", "32");
        Assert.Equal(32, cropped.Width);
        Assert.Equal(height, cropped.Height);
        Assert.All(cropped.Pixels, pixel => Assert.Equal(0, pixel.Alpha));

        using var combined = Export("combined.png", "--width", "64", "--scale", "0.5");
        Assert.Equal(32, combined.Width);
        Assert.Equal(height / 2, combined.Height);
        Assert.All(combined.Pixels, pixel => Assert.Equal(0, pixel.Alpha));

        using var padded = Export("padded.png", "--width", "160", "--scale", "0.25");
        Assert.Equal(40, padded.Width);
        Assert.Equal(height / 4, padded.Height);
        Assert.Equal(SKColors.Red, padded.GetPixel(28, padded.Height / 2));
        Assert.Equal(0, padded.GetPixel(36, padded.Height / 2).Alpha);

        using var subtree = Export("node.png", "--node", "33333333-3333-3333-3333-333333333333", "--scale", "0.25");
        Assert.Equal(8, subtree.Width);
        Assert.Equal(height / 4, subtree.Height);
        Assert.All(subtree.Pixels, pixel => Assert.Equal(SKColors.Red, pixel));

        // Standalone SVG supports the same viewport overrides; asset manifests
        // separately restrict their width/height options to raster entries.
        var svgPath = Path.Combine(TestDir, "viewport.svg");
        var svgResult = RunCommand("export", path, "-t", "svg", "--width", "64", "--height", "32",
            "--scale", "0.25", "-o", svgPath, "-f", "json");
        Assert.True(svgResult.ExitCode == 0, svgResult.Output + svgResult.Error);
        var svg = XDocument.Load(svgPath).Root!;
        Assert.Equal("0 0 64 32", svg.Attribute("viewBox")!.Value);
        Assert.Equal("16", svg.Attribute("width")!.Value);
        Assert.Equal("8", svg.Attribute("height")!.Value);

        SKBitmap Export(string name, params string[] options)
        {
            var output = Path.Combine(TestDir, name);
            var result = RunCommand(["export", path, "-t", "png", "-o", output, "-f", "json", .. options]);
            Assert.True(result.ExitCode == 0, result.Output + result.Error);
            return SKBitmap.Decode(output) ?? throw new InvalidOperationException("PNG was not decodable");
        }
    }
}
