namespace FTPlus.AgentGraphicDesign.Core.Tokens;

/// <summary>
/// Bundled color palettes with proper license attribution.
/// </summary>
public static class BundledPalettes
{
    private static IReadOnlyDictionary<string, BundledPalette>? _all;

    public static IReadOnlyDictionary<string, BundledPalette> All => _all ??= new Dictionary<string, BundledPalette>
    {
        ["tailwind"] = Tailwind,
        ["material"] = Material,
        ["colorbrewer-qualitative"] = ColorBrewerQualitative,
    };

    /// <summary>
    /// Tailwind CSS default color palette.
    /// License: MIT - https://github.com/tailwindlabs/tailwindcss/blob/master/LICENSE
    /// </summary>
    public static BundledPalette Tailwind { get; } = new(
        Name: "tailwind",
        Description: "Tailwind CSS default color palette",
        License: "MIT",
        LicenseUrl: "https://github.com/tailwindlabs/tailwindcss/blob/master/LICENSE",
        Attribution: "Tailwind CSS (c) Tailwind Labs, Inc.",
        Colors: new Dictionary<string, string>
        {
            // Slate
            ["slate-50"] = "#f8fafc",
            ["slate-100"] = "#f1f5f9",
            ["slate-200"] = "#e2e8f0",
            ["slate-300"] = "#cbd5e1",
            ["slate-400"] = "#94a3b8",
            ["slate-500"] = "#64748b",
            ["slate-600"] = "#475569",
            ["slate-700"] = "#334155",
            ["slate-800"] = "#1e293b",
            ["slate-900"] = "#0f172a",
            ["slate-950"] = "#020617",
            // Gray
            ["gray-50"] = "#f9fafb",
            ["gray-100"] = "#f3f4f6",
            ["gray-200"] = "#e5e7eb",
            ["gray-300"] = "#d1d5db",
            ["gray-400"] = "#9ca3af",
            ["gray-500"] = "#6b7280",
            ["gray-600"] = "#4b5563",
            ["gray-700"] = "#374151",
            ["gray-800"] = "#1f2937",
            ["gray-900"] = "#111827",
            ["gray-950"] = "#030712",
            // Red
            ["red-50"] = "#fef2f2",
            ["red-100"] = "#fee2e2",
            ["red-200"] = "#fecaca",
            ["red-300"] = "#fca5a5",
            ["red-400"] = "#f87171",
            ["red-500"] = "#ef4444",
            ["red-600"] = "#dc2626",
            ["red-700"] = "#b91c1c",
            ["red-800"] = "#991b1b",
            ["red-900"] = "#7f1d1d",
            ["red-950"] = "#450a0a",
            // Orange
            ["orange-50"] = "#fff7ed",
            ["orange-100"] = "#ffedd5",
            ["orange-200"] = "#fed7aa",
            ["orange-300"] = "#fdba74",
            ["orange-400"] = "#fb923c",
            ["orange-500"] = "#f97316",
            ["orange-600"] = "#ea580c",
            ["orange-700"] = "#c2410c",
            ["orange-800"] = "#9a3412",
            ["orange-900"] = "#7c2d12",
            ["orange-950"] = "#431407",
            // Yellow
            ["yellow-50"] = "#fefce8",
            ["yellow-100"] = "#fef9c3",
            ["yellow-200"] = "#fef08a",
            ["yellow-300"] = "#fde047",
            ["yellow-400"] = "#facc15",
            ["yellow-500"] = "#eab308",
            ["yellow-600"] = "#ca8a04",
            ["yellow-700"] = "#a16207",
            ["yellow-800"] = "#854d0e",
            ["yellow-900"] = "#713f12",
            ["yellow-950"] = "#422006",
            // Green
            ["green-50"] = "#f0fdf4",
            ["green-100"] = "#dcfce7",
            ["green-200"] = "#bbf7d0",
            ["green-300"] = "#86efac",
            ["green-400"] = "#4ade80",
            ["green-500"] = "#22c55e",
            ["green-600"] = "#16a34a",
            ["green-700"] = "#15803d",
            ["green-800"] = "#166534",
            ["green-900"] = "#14532d",
            ["green-950"] = "#052e16",
            // Blue
            ["blue-50"] = "#eff6ff",
            ["blue-100"] = "#dbeafe",
            ["blue-200"] = "#bfdbfe",
            ["blue-300"] = "#93c5fd",
            ["blue-400"] = "#60a5fa",
            ["blue-500"] = "#3b82f6",
            ["blue-600"] = "#2563eb",
            ["blue-700"] = "#1d4ed8",
            ["blue-800"] = "#1e40af",
            ["blue-900"] = "#1e3a8a",
            ["blue-950"] = "#172554",
            // Indigo
            ["indigo-50"] = "#eef2ff",
            ["indigo-100"] = "#e0e7ff",
            ["indigo-200"] = "#c7d2fe",
            ["indigo-300"] = "#a5b4fc",
            ["indigo-400"] = "#818cf8",
            ["indigo-500"] = "#6366f1",
            ["indigo-600"] = "#4f46e5",
            ["indigo-700"] = "#4338ca",
            ["indigo-800"] = "#3730a3",
            ["indigo-900"] = "#312e81",
            ["indigo-950"] = "#1e1b4b",
            // Purple
            ["purple-50"] = "#faf5ff",
            ["purple-100"] = "#f3e8ff",
            ["purple-200"] = "#e9d5ff",
            ["purple-300"] = "#d8b4fe",
            ["purple-400"] = "#c084fc",
            ["purple-500"] = "#a855f7",
            ["purple-600"] = "#9333ea",
            ["purple-700"] = "#7e22ce",
            ["purple-800"] = "#6b21a8",
            ["purple-900"] = "#581c87",
            ["purple-950"] = "#3b0764",
            // Pink
            ["pink-50"] = "#fdf2f8",
            ["pink-100"] = "#fce7f3",
            ["pink-200"] = "#fbcfe8",
            ["pink-300"] = "#f9a8d4",
            ["pink-400"] = "#f472b6",
            ["pink-500"] = "#ec4899",
            ["pink-600"] = "#db2777",
            ["pink-700"] = "#be185d",
            ["pink-800"] = "#9d174d",
            ["pink-900"] = "#831843",
            ["pink-950"] = "#500724",
        }
    );

    /// <summary>
    /// Material Design color palette (2014 spec).
    /// License: Apache 2.0 - https://www.apache.org/licenses/LICENSE-2.0
    /// </summary>
    public static BundledPalette Material { get; } = new(
        Name: "material",
        Description: "Material Design color palette",
        License: "Apache-2.0",
        LicenseUrl: "https://www.apache.org/licenses/LICENSE-2.0",
        Attribution: "Material Design (c) Google LLC",
        Colors: new Dictionary<string, string>
        {
            // Red
            ["red-50"] = "#ffebee",
            ["red-100"] = "#ffcdd2",
            ["red-200"] = "#ef9a9a",
            ["red-300"] = "#e57373",
            ["red-400"] = "#ef5350",
            ["red-500"] = "#f44336",
            ["red-600"] = "#e53935",
            ["red-700"] = "#d32f2f",
            ["red-800"] = "#c62828",
            ["red-900"] = "#b71c1c",
            // Pink
            ["pink-50"] = "#fce4ec",
            ["pink-100"] = "#f8bbd0",
            ["pink-200"] = "#f48fb1",
            ["pink-300"] = "#f06292",
            ["pink-400"] = "#ec407a",
            ["pink-500"] = "#e91e63",
            ["pink-600"] = "#d81b60",
            ["pink-700"] = "#c2185b",
            ["pink-800"] = "#ad1457",
            ["pink-900"] = "#880e4f",
            // Purple
            ["purple-50"] = "#f3e5f5",
            ["purple-100"] = "#e1bee7",
            ["purple-200"] = "#ce93d8",
            ["purple-300"] = "#ba68c8",
            ["purple-400"] = "#ab47bc",
            ["purple-500"] = "#9c27b0",
            ["purple-600"] = "#8e24aa",
            ["purple-700"] = "#7b1fa2",
            ["purple-800"] = "#6a1b9a",
            ["purple-900"] = "#4a148c",
            // Deep Purple
            ["deep-purple-50"] = "#ede7f6",
            ["deep-purple-100"] = "#d1c4e9",
            ["deep-purple-200"] = "#b39ddb",
            ["deep-purple-300"] = "#9575cd",
            ["deep-purple-400"] = "#7e57c2",
            ["deep-purple-500"] = "#673ab7",
            ["deep-purple-600"] = "#5e35b1",
            ["deep-purple-700"] = "#512da8",
            ["deep-purple-800"] = "#4527a0",
            ["deep-purple-900"] = "#311b92",
            // Indigo
            ["indigo-50"] = "#e8eaf6",
            ["indigo-100"] = "#c5cae9",
            ["indigo-200"] = "#9fa8da",
            ["indigo-300"] = "#7986cb",
            ["indigo-400"] = "#5c6bc0",
            ["indigo-500"] = "#3f51b5",
            ["indigo-600"] = "#3949ab",
            ["indigo-700"] = "#303f9f",
            ["indigo-800"] = "#283593",
            ["indigo-900"] = "#1a237e",
            // Blue
            ["blue-50"] = "#e3f2fd",
            ["blue-100"] = "#bbdefb",
            ["blue-200"] = "#90caf9",
            ["blue-300"] = "#64b5f6",
            ["blue-400"] = "#42a5f5",
            ["blue-500"] = "#2196f3",
            ["blue-600"] = "#1e88e5",
            ["blue-700"] = "#1976d2",
            ["blue-800"] = "#1565c0",
            ["blue-900"] = "#0d47a1",
            // Green
            ["green-50"] = "#e8f5e9",
            ["green-100"] = "#c8e6c9",
            ["green-200"] = "#a5d6a7",
            ["green-300"] = "#81c784",
            ["green-400"] = "#66bb6a",
            ["green-500"] = "#4caf50",
            ["green-600"] = "#43a047",
            ["green-700"] = "#388e3c",
            ["green-800"] = "#2e7d32",
            ["green-900"] = "#1b5e20",
            // Teal
            ["teal-50"] = "#e0f2f1",
            ["teal-100"] = "#b2dfdb",
            ["teal-200"] = "#80cbc4",
            ["teal-300"] = "#4db6ac",
            ["teal-400"] = "#26a69a",
            ["teal-500"] = "#009688",
            ["teal-600"] = "#00897b",
            ["teal-700"] = "#00796b",
            ["teal-800"] = "#00695c",
            ["teal-900"] = "#004d40",
            // Orange
            ["orange-50"] = "#fff3e0",
            ["orange-100"] = "#ffe0b2",
            ["orange-200"] = "#ffcc80",
            ["orange-300"] = "#ffb74d",
            ["orange-400"] = "#ffa726",
            ["orange-500"] = "#ff9800",
            ["orange-600"] = "#fb8c00",
            ["orange-700"] = "#f57c00",
            ["orange-800"] = "#ef6c00",
            ["orange-900"] = "#e65100",
            // Grey
            ["grey-50"] = "#fafafa",
            ["grey-100"] = "#f5f5f5",
            ["grey-200"] = "#eeeeee",
            ["grey-300"] = "#e0e0e0",
            ["grey-400"] = "#bdbdbd",
            ["grey-500"] = "#9e9e9e",
            ["grey-600"] = "#757575",
            ["grey-700"] = "#616161",
            ["grey-800"] = "#424242",
            ["grey-900"] = "#212121",
        }
    );

    /// <summary>
    /// ColorBrewer qualitative palette (Set3).
    /// License: Apache 2.0 - https://github.com/axismaps/colorbrewer/blob/master/LICENCE.txt
    /// </summary>
    public static BundledPalette ColorBrewerQualitative { get; } = new(
        Name: "colorbrewer-qualitative",
        Description: "ColorBrewer Set3 qualitative palette for categorical data",
        License: "Apache-2.0",
        LicenseUrl: "https://github.com/axismaps/colorbrewer/blob/master/LICENCE.txt",
        Attribution: "ColorBrewer (c) Cynthia Brewer, Mark Harrower, and The Pennsylvania State University",
        Colors: new Dictionary<string, string>
        {
            // Set3 - 12 colors for qualitative/categorical data
            ["set3-1"] = "#8dd3c7",
            ["set3-2"] = "#ffffb3",
            ["set3-3"] = "#bebada",
            ["set3-4"] = "#fb8072",
            ["set3-5"] = "#80b1d3",
            ["set3-6"] = "#fdb462",
            ["set3-7"] = "#b3de69",
            ["set3-8"] = "#fccde5",
            ["set3-9"] = "#d9d9d9",
            ["set3-10"] = "#bc80bd",
            ["set3-11"] = "#ccebc5",
            ["set3-12"] = "#ffed6f",
            // Paired - 12 colors for paired categorical data
            ["paired-1"] = "#a6cee3",
            ["paired-2"] = "#1f78b4",
            ["paired-3"] = "#b2df8a",
            ["paired-4"] = "#33a02c",
            ["paired-5"] = "#fb9a99",
            ["paired-6"] = "#e31a1c",
            ["paired-7"] = "#fdbf6f",
            ["paired-8"] = "#ff7f00",
            ["paired-9"] = "#cab2d6",
            ["paired-10"] = "#6a3d9a",
            ["paired-11"] = "#ffff99",
            ["paired-12"] = "#b15928",
        }
    );
}

/// <summary>
/// A bundled color palette with license information.
/// </summary>
public sealed record BundledPalette(
    string Name,
    string Description,
    string License,
    string LicenseUrl,
    string Attribution,
    IReadOnlyDictionary<string, string> Colors
);
