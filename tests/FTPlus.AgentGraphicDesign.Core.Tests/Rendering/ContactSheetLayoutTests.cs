using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Rendering;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Rendering;

/// <summary>
/// Contact-sheet grid geometry: column defaulting, row count, pixel dimensions,
/// cell placement, aspect-fit, and the input guards. Pure layout, no rendering.
/// </summary>
public class ContactSheetLayoutTests
{
    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2, 2, 1)]
    [InlineData(3, 2, 2)]
    [InlineData(4, 2, 2)]
    [InlineData(9, 3, 3)]
    [InlineData(10, 4, 3)]
    public void Compute_DefaultsColumnsToCeilSqrt(int count, int expectedColumns, int expectedRows)
    {
        var layout = ContactSheetLayout.Compute(count, ContactSheetOptions.Default);

        Assert.Equal(count, layout.Count);
        Assert.Equal(expectedColumns, layout.Columns);
        Assert.Equal(expectedRows, layout.Rows);
    }

    [Fact]
    public void Compute_HonorsExplicitColumns()
    {
        var layout = ContactSheetLayout.Compute(6, ContactSheetOptions.Default with { Columns = 3 });

        Assert.Equal(3, layout.Columns);
        Assert.Equal(2, layout.Rows);
    }

    [Fact]
    public void Compute_ClampsColumnsToCount()
    {
        var layout = ContactSheetLayout.Compute(2, ContactSheetOptions.Default with { Columns = 8 });

        Assert.Equal(2, layout.Columns);
        Assert.Equal(1, layout.Rows);
    }

    [Fact]
    public void Compute_DimensionsIncludeGutters()
    {
        var options = new ContactSheetOptions { Columns = 2, CellSize = 128, Gap = 8, Labels = false };
        var layout = ContactSheetLayout.Compute(4, options);

        // gap + columns * (cell + gap)
        Assert.Equal(8 + 2 * (128 + 8), layout.Width);
        Assert.Equal(8 + 2 * (128 + 8), layout.Height);
        Assert.Equal(0, layout.LabelBand);
    }

    [Fact]
    public void Compute_LabelsAddABandPerRow()
    {
        var noLabels = ContactSheetLayout.Compute(4, new ContactSheetOptions { Columns = 2, CellSize = 100, Gap = 10 });
        var labelled = ContactSheetLayout.Compute(4, new ContactSheetOptions { Columns = 2, CellSize = 100, Gap = 10, Labels = true });

        Assert.Equal(ContactSheetLayout.LabelHeight, labelled.LabelBand);
        Assert.Equal(noLabels.Width, labelled.Width);
        Assert.Equal(noLabels.Height + labelled.Rows * ContactSheetLayout.LabelHeight, labelled.Height);
    }

    [Fact]
    public void CellBox_PlacesEntriesInRowMajorOrder()
    {
        var layout = ContactSheetLayout.Compute(4, new ContactSheetOptions { Columns = 2, CellSize = 100, Gap = 10 });

        var first = layout.CellBox(0);
        Assert.Equal(10, first.X);
        Assert.Equal(10, first.Y);
        Assert.Equal(100, first.Width);
        Assert.Equal(100, first.Height);

        var second = layout.CellBox(1);
        Assert.Equal(10 + 100 + 10, second.X);
        Assert.Equal(10, second.Y);

        var third = layout.CellBox(2);
        Assert.Equal(10, third.X);
        Assert.Equal(10 + 100 + 10, third.Y);
    }

    [Fact]
    public void CellBox_RejectsOutOfRangeIndex()
    {
        var layout = ContactSheetLayout.Compute(2, ContactSheetOptions.Default);
        Assert.Throws<ArgumentOutOfRangeException>(() => layout.CellBox(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => layout.CellBox(-1));
    }

    [Fact]
    public void Fit_PreservesAspectRatioAndCentres()
    {
        var cell = new PixelBox(0, 0, 100, 100);

        // A 2:1 canvas fits to full width and half height, centred vertically.
        var wide = ContactSheetLayout.Fit(200, 100, cell);
        Assert.Equal(100, wide.Width);
        Assert.Equal(50, wide.Height);
        Assert.Equal(0, wide.X);
        Assert.Equal(25, wide.Y);
    }

    [Fact]
    public void Fit_NeverShrinksBelowOnePixel()
    {
        var cell = new PixelBox(0, 0, 10, 10);
        var sliver = ContactSheetLayout.Fit(1000, 1, cell);
        Assert.True(sliver.Width >= 1);
        Assert.True(sliver.Height >= 1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(ContactSheetLayout.MaxEntries + 1)]
    public void Compute_RejectsBadCount(int count)
    {
        Assert.Throws<DocumentEngineException>(() => ContactSheetLayout.Compute(count, ContactSheetOptions.Default));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(ContactSheetLayout.MaxColumns + 1)]
    public void Compute_RejectsBadColumns(int columns)
    {
        Assert.Throws<DocumentEngineException>(() =>
            ContactSheetLayout.Compute(4, ContactSheetOptions.Default with { Columns = columns }));
    }

    [Fact]
    public void Compute_RejectsOutOfRangeCellSize()
    {
        Assert.Throws<DocumentEngineException>(() =>
            ContactSheetLayout.Compute(4, ContactSheetOptions.Default with { CellSize = ContactSheetOptions.MinCellSize - 1 }));
        Assert.Throws<DocumentEngineException>(() =>
            ContactSheetLayout.Compute(4, ContactSheetOptions.Default with { CellSize = ContactSheetOptions.MaxCellSize + 1 }));
    }
}
