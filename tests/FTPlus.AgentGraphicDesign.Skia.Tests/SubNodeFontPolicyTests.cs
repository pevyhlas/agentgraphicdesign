using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Rendering;
using FTPlus.AgentGraphicDesign.Core.Serialization;
using FTPlus.AgentGraphicDesign.Tests.Characterization;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Skia.Tests;

public sealed class SubNodeFontPolicyTests
{
    private static DocumentEngine Engine() => new(renderer: new SkiaRenderer(),
        measurerFactory: policy => new SkiaTextMeasurer(policy));

    [Fact]
    public void ExplicitBundledBoldDirectoryChangesTheActualCropAndMatchesMeasuredBounds()
    {
        var directory = Directory.CreateTempSubdirectory("agd-subnode-fonts-").FullName;
        try
        {
            File.Copy(LegacyContactSheetFixture.BundledBoldFontPath, Path.Combine(directory, "Inter-Bold.ttf"));
            var text = new TextNode { Text = "Wide lettering", FontFamily = "Inter", FontSize = 48 };
            var source = Scene(text);
            var before = DocumentSerializer.Serialize(source);
            var engine = Engine();
            var plain = engine.RenderNode(source, text.Id, new RenderOptions { SystemFonts = false }, CancellationToken.None);
            var options = new RenderOptions { FontDirectories = [directory], SystemFonts = false, StrictFonts = true };
            var bold = engine.RenderNode(source, text.Id, options, CancellationToken.None);
            var expected = engine.GetBounds(source, text.Id,
                new FontPolicy { FontDirectories = [directory], SystemFonts = false }, CancellationToken.None).Single().World!.Value;
            Assert.Equal((int)Math.Round(expected.Width), bold.Width);
            Assert.NotEqual(plain.Width, bold.Width);
            Assert.Equal(before, DocumentSerializer.Serialize(source));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void StrictFailureOnlyAppliesToRenderedText(bool hideText, bool hideAncestor)
    {
        var text = new TextNode { Text = "Unknown", FontFamily = "MissingSubnodeFamily9Z", Visible = !hideText };
        var branch = new GroupNode { Visible = !hideAncestor, Children = [text], Order = "a1" };
        var target = new FrameNode
        {
            Width = 100,
            Height = 50,
            ClipContent = true,
            Children = [new RectangleNode { Width = 5, Height = 5, Order = "a0" }, branch]
        };
        var source = Scene(target);
        var before = DocumentSerializer.Serialize(source);
        var engine = Engine();
        var options = new RenderOptions { SystemFonts = false, StrictFonts = true };
        if (!hideText && !hideAncestor)
        {
            var error = Assert.Throws<DocumentEngineException>(() => engine.RenderNode(source, target.Id, options, CancellationToken.None));
            Assert.Equal(EngineErrors.FontNotFound, error.Code);
        }
        else
        {
            var result = engine.RenderNode(source, target.Id, options, CancellationToken.None);
            Assert.Equal((100, 50), (result.Width, result.Height));
            Assert.DoesNotContain(result.Diagnostics, d => d.Message.Contains("MissingSubnodeFamily9Z", StringComparison.Ordinal));
        }
        Assert.Equal(before, DocumentSerializer.Serialize(source));
    }

    [Fact]
    public void HiddenGeometryStillContributesToLegacyCropWithoutStrictFailure()
    {
        var hidden = new TextNode { Text = "Hidden text extent", FontFamily = "MissingSubnodeFamily9Z", FontSize = 48, Visible = false, Order = "a1" };
        var target = new GroupNode { Children = [new RectangleNode { Width = 5, Height = 5, Order = "a0" }, hidden] };
        var source = Scene(target);
        var engine = Engine();
        var expected = engine.GetBounds(source, target.Id, new FontPolicy { SystemFonts = false }, CancellationToken.None).Single().World!.Value;
        var result = engine.RenderNode(source, target.Id,
            new RenderOptions { SystemFonts = false, StrictFonts = true }, CancellationToken.None);
        Assert.Equal((int)Math.Round(expected.Width), result.Width);
        Assert.True(result.Width > 100);
        Assert.DoesNotContain(result.Diagnostics, d => d.Message.Contains("MissingSubnodeFamily9Z", StringComparison.Ordinal));
    }

    private static Document Scene(Node node) => new()
    {
        Id = Guid.NewGuid(),
        Canvas = new Canvas { Width = 1000, Height = 1000 },
        Root = new FrameNode { Width = 1000, Height = 1000, ClipContent = false, Children = [node] }
    };
}
