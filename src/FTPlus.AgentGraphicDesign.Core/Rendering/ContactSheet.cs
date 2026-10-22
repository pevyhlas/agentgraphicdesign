using System.Globalization;
using FTPlus.AgentGraphicDesign.Core.Documents;

namespace FTPlus.AgentGraphicDesign.Core.Rendering;

/// <summary>One tile of a contact sheet: a document plus the label printed under it.</summary>
public sealed record ContactSheetEntry(Document Document, string Label);

/// <summary>Grid options for <see cref="IDocumentEngine.RenderContactSheet"/>.</summary>
public sealed record ContactSheetOptions
{
    public const int DefaultCellSize = 128;
    public const int DefaultGap = 8;

    /// <summary>A light neutral that keeps both dark and light artwork readable.</summary>
    public const string DefaultBackground = "#f4f4f5";

    public const int MinCellSize = 8;
    public const int MaxCellSize = 4096;
    public const int MaxGap = 1024;

    public static ContactSheetOptions Default { get; } = new();

    /// <summary>Columns in the grid; null means <c>ceil(sqrt(count))</c>.</summary>
    public int? Columns { get; init; }

    /// <summary>Square cell size in pixels; each document is aspect-fitted into it.</summary>
    public int CellSize { get; init; } = DefaultCellSize;

    /// <summary>Gap between cells and around the sheet edge, in pixels.</summary>
    public int Gap { get; init; } = DefaultGap;

    /// <summary>Sheet background color; null means <see cref="DefaultBackground"/>.</summary>
    public string? Background { get; init; }

    /// <summary>Print each entry's label under its cell.</summary>
    public bool Labels { get; init; }

    /// <summary>
    /// Directories searched for fonts before the bundled family and system fonts, applied to
    /// every tile render and to the labels so the sheet matches a normal export of each tile.
    /// </summary>
    public IReadOnlyList<string> FontDirectories { get; init; } = [];

    /// <summary>Whether host-installed fonts may satisfy families the directories and bundled family do not provide.</summary>
    public bool SystemFonts { get; init; } = true;

    /// <summary>Fail a tile render with <see cref="EngineErrors.FontNotFound"/> instead of substituting the bundled family.</summary>
    public bool StrictFonts { get; init; }

    /// <summary>The tile/label font policy gathered from <see cref="FontDirectories"/>, <see cref="SystemFonts"/>, and <see cref="StrictFonts"/>.</summary>
    public FontPolicy FontPolicy => new()
    {
        FontDirectories = FontDirectories,
        SystemFonts = SystemFonts,
        StrictFonts = StrictFonts
    };
}

/// <summary>An integer pixel rectangle on the sheet.</summary>
public readonly record struct PixelBox(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;
}

/// <summary>Where one entry landed: its grid position, its cell box, and the aspect-fitted image box inside it.</summary>
public sealed record ContactSheetCell(int Index, string Label, int Column, int Row, PixelBox Cell, PixelBox Image);

/// <summary>
/// Pure grid arithmetic for a contact sheet, shared by every surface so limits can be
/// checked before any pixel is rendered. The dimension limits mirror the Skia renderer.
/// </summary>
public sealed record ContactSheetLayout
{
    public const int MaxEntries = 256;
    public const int MaxColumns = MaxEntries;

    /// <summary>Height of the label band under each cell when labels are on.</summary>
    public const int LabelHeight = 20;

    /// <summary>Largest sheet edge in pixels; matches the raster renderer's limit.</summary>
    public const int MaxDimension = 8192;

    /// <summary>Largest sheet area in pixels; matches the raster renderer's limit.</summary>
    public const long MaxPixelCount = 64_000_000;

    public required int Count { get; init; }

    public required int Columns { get; init; }

    public required int Rows { get; init; }

    public required int CellSize { get; init; }

    public required int Gap { get; init; }

    /// <summary>Label band height per row: <see cref="LabelHeight"/> with labels, otherwise 0.</summary>
    public required int LabelBand { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    /// <summary>
    /// Validates the options against <paramref name="count"/> entries and computes the grid.
    /// Throws <see cref="DocumentEngineException"/> with <see cref="EngineErrors.InvalidArguments"/>,
    /// <see cref="EngineErrors.InvalidProperty"/>, or <see cref="EngineErrors.InvalidDimensions"/>.
    /// </summary>
    public static ContactSheetLayout Compute(int count, ContactSheetOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (count < 1)
            throw new DocumentEngineException(EngineErrors.InvalidArguments, "A contact sheet needs at least one document");
        if (count > MaxEntries)
            throw new DocumentEngineException(EngineErrors.InvalidArguments, $"A contact sheet can hold at most {MaxEntries} documents");
        if (options.Columns is { } requestedColumns && (requestedColumns < 1 || requestedColumns > MaxColumns))
            throw new DocumentEngineException(EngineErrors.InvalidProperty, $"Columns must be between 1 and {MaxColumns}");
        if (options.CellSize < ContactSheetOptions.MinCellSize || options.CellSize > ContactSheetOptions.MaxCellSize)
        {
            throw new DocumentEngineException(EngineErrors.InvalidProperty,
                $"Cell size must be between {ContactSheetOptions.MinCellSize} and {ContactSheetOptions.MaxCellSize}");
        }
        if (options.Gap < 0 || options.Gap > ContactSheetOptions.MaxGap)
            throw new DocumentEngineException(EngineErrors.InvalidProperty, $"Gap must be between 0 and {ContactSheetOptions.MaxGap}");
        if (options.Background is { } background && !DocumentLimits.IsValidColor(background))
            throw new DocumentEngineException(EngineErrors.InvalidProperty, $"Invalid background color: '{background}'");

        var columns = options.Columns ?? (int)Math.Ceiling(Math.Sqrt(count));
        columns = Math.Clamp(columns, 1, count);
        var rows = (count + columns - 1) / columns;
        var labelBand = options.Labels ? LabelHeight : 0;
        var width = (long)options.Gap + (long)columns * (options.CellSize + options.Gap);
        var height = (long)options.Gap + (long)rows * (options.CellSize + labelBand + options.Gap);
        if (width > MaxDimension || height > MaxDimension)
        {
            throw new DocumentEngineException(EngineErrors.InvalidDimensions,
                $"Contact sheet dimensions exceed maximum ({MaxDimension}); got {width}x{height}");
        }
        if (width * height > MaxPixelCount)
        {
            throw new DocumentEngineException(EngineErrors.InvalidDimensions,
                $"Contact sheet pixel count exceeds maximum ({MaxPixelCount.ToString("N0", CultureInfo.InvariantCulture)}); got {width}x{height}");
        }

        return new ContactSheetLayout
        {
            Count = count,
            Columns = columns,
            Rows = rows,
            CellSize = options.CellSize,
            Gap = options.Gap,
            LabelBand = labelBand,
            Width = (int)width,
            Height = (int)height
        };
    }

    /// <summary>The square cell box of the entry at <paramref name="index"/>.</summary>
    public PixelBox CellBox(int index)
    {
        if (index < 0 || index >= Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        var column = index % Columns;
        var row = index / Columns;
        return new PixelBox(
            Gap + column * (CellSize + Gap),
            Gap + row * (CellSize + LabelBand + Gap),
            CellSize,
            CellSize);
    }

    /// <summary>The label band directly under the cell at <paramref name="index"/>; empty when labels are off.</summary>
    public PixelBox LabelBox(int index)
    {
        var cell = CellBox(index);
        return new PixelBox(cell.X, cell.Bottom, cell.Width, LabelBand);
    }

    /// <summary>
    /// Aspect-fits a canvas of <paramref name="width"/> x <paramref name="height"/> into
    /// <paramref name="cell"/>, centred, never smaller than one pixel per side.
    /// </summary>
    public static PixelBox Fit(double width, double height, PixelBox cell)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
        {
            throw new DocumentEngineException(EngineErrors.InvalidDimensions,
                $"Canvas dimensions must be positive and finite; got {width.ToString(CultureInfo.InvariantCulture)}x{height.ToString(CultureInfo.InvariantCulture)}");
        }

        var scale = Math.Min(cell.Width / width, cell.Height / height);
        var fittedWidth = Math.Clamp((int)Math.Round(width * scale, MidpointRounding.AwayFromZero), 1, cell.Width);
        var fittedHeight = Math.Clamp((int)Math.Round(height * scale, MidpointRounding.AwayFromZero), 1, cell.Height);
        return new PixelBox(
            cell.X + (cell.Width - fittedWidth) / 2,
            cell.Y + (cell.Height - fittedHeight) / 2,
            fittedWidth,
            fittedHeight);
    }

    /// <summary>The cell records for <paramref name="entries"/> in sheet order.</summary>
    public IReadOnlyList<ContactSheetCell> Cells(IReadOnlyList<ContactSheetEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count != Count)
            throw new ArgumentException($"Expected {Count} entries; got {entries.Count}", nameof(entries));

        var cells = new ContactSheetCell[entries.Count];
        for (var i = 0; i < entries.Count; i++)
        {
            var cell = CellBox(i);
            var canvas = entries[i].Document.Canvas;
            cells[i] = new ContactSheetCell(i, entries[i].Label, i % Columns, i / Columns, cell, Fit(canvas.Width, canvas.Height, cell));
        }
        return cells;
    }
}

/// <summary>A rendered contact sheet: PNG bytes plus the grid that produced them.</summary>
public sealed record ContactSheetResult
{
    public required byte[] Data { get; init; }

    public required string MimeType { get; init; }

    public required ContactSheetLayout Layout { get; init; }

    public required IReadOnlyList<ContactSheetCell> Cells { get; init; }

    public int Width => Layout.Width;

    public int Height => Layout.Height;
}

/// <summary>
/// Composes several documents into one raster grid. Implemented by the Skia project;
/// the core engine only validates the layout and delegates.
/// </summary>
public interface IContactSheetRenderer
{
    ContactSheetResult Render(IReadOnlyList<ContactSheetEntry> entries, ContactSheetOptions options, CancellationToken cancellationToken);
}
