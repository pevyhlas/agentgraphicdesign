using System.Text.Json;
using System.Text.Json.Serialization;

namespace FTPlus.AgentGraphicDesign.Core.Documents.Nodes;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToNearestAncestor)]
[JsonDerivedType(typeof(FrameNode), "frame")]
[JsonDerivedType(typeof(RectangleNode), "rectangle")]
[JsonDerivedType(typeof(EllipseNode), "ellipse")]
[JsonDerivedType(typeof(PathNode), "path")]
[JsonDerivedType(typeof(TextNode), "text")]
[JsonDerivedType(typeof(GroupNode), "group")]
[JsonDerivedType(typeof(LineNode), "line")]
[JsonDerivedType(typeof(PolygonNode), "polygon")]
[JsonDerivedType(typeof(ImageNode), "image")]
[JsonDerivedType(typeof(UnknownNode), "unknown")]
public abstract class Node
{
    [JsonPropertyName("id")]
    [JsonRequired]
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonPropertyName("order")]
    public string Order { get; set; } = "a0";

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("visible")]
    public bool Visible { get; set; } = true;

    [JsonPropertyName("locked")]
    public bool Locked { get; set; }

    [JsonPropertyName("transform")]
    public Transform? Transform { get; set; }

    [JsonPropertyName("style")]
    public Style? Style { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class Transform
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("rotation")]
    public double Rotation { get; set; }

    [JsonPropertyName("scaleX")]
    public double ScaleX { get; set; } = 1;

    [JsonPropertyName("scaleY")]
    public double ScaleY { get; set; } = 1;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class Style
{
    [JsonPropertyName("fill")]
    public Fill? Fill { get; set; }

    [JsonPropertyName("stroke")]
    public Stroke? Stroke { get; set; }

    [JsonPropertyName("opacity")]
    public double Opacity { get; set; } = 1;

    [JsonPropertyName("blendMode")]
    public string? BlendMode { get; set; }

    /// <summary>
    /// Fill rule for path and polygon nodes: <see cref="FillRules.NonZero"/> (the default
    /// when null) or <see cref="FillRules.EvenOdd"/>. Other node types ignore it.
    /// </summary>
    [JsonPropertyName("fillRule")]
    public string? FillRule { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class Fill
{
    [JsonPropertyName("color")]
    public string? Color { get; set; }

    [JsonPropertyName("tokenRef")]
    public string? TokenRef { get; set; }

    [JsonPropertyName("gradient")]
    public Gradient? Gradient { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(LinearGradient), "linear")]
[JsonDerivedType(typeof(RadialGradient), "radial")]
public abstract class Gradient
{
    [JsonPropertyName("stops")]
    public List<GradientStop> Stops { get; set; } = [];
}

public sealed class LinearGradient : Gradient
{
    [JsonPropertyName("x1")]
    public double X1 { get; set; }

    [JsonPropertyName("y1")]
    public double Y1 { get; set; }

    [JsonPropertyName("x2")]
    public double X2 { get; set; } = 1;

    [JsonPropertyName("y2")]
    public double Y2 { get; set; }
}

public sealed class RadialGradient : Gradient
{
    [JsonPropertyName("cx")]
    public double Cx { get; set; } = 0.5;

    [JsonPropertyName("cy")]
    public double Cy { get; set; } = 0.5;

    [JsonPropertyName("r")]
    public double R { get; set; } = 0.5;

    [JsonPropertyName("fx")]
    public double? Fx { get; set; }

    [JsonPropertyName("fy")]
    public double? Fy { get; set; }
}

public sealed class GradientStop
{
    [JsonPropertyName("offset")]
    public double Offset { get; set; }

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#000000";

    [JsonPropertyName("opacity")]
    public double Opacity { get; set; } = 1;
}

public sealed class Stroke
{
    [JsonPropertyName("color")]
    public string? Color { get; set; }

    [JsonPropertyName("tokenRef")]
    public string? TokenRef { get; set; }

    [JsonPropertyName("width")]
    public double Width { get; set; } = 1;

    [JsonPropertyName("cap")]
    public StrokeCap Cap { get; set; } = StrokeCap.Butt;

    [JsonPropertyName("join")]
    public StrokeJoin Join { get; set; } = StrokeJoin.Miter;

    [JsonPropertyName("miterLimit")]
    public double MiterLimit { get; set; } = 4;

    [JsonPropertyName("dashArray")]
    public double[]? DashArray { get; set; }

    [JsonPropertyName("dashOffset")]
    public double DashOffset { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter<StrokeCap>))]
public enum StrokeCap
{
    [JsonStringEnumMemberName("butt")]
    Butt,

    [JsonStringEnumMemberName("round")]
    Round,

    [JsonStringEnumMemberName("square")]
    Square
}

[JsonConverter(typeof(JsonStringEnumConverter<StrokeJoin>))]
public enum StrokeJoin
{
    [JsonStringEnumMemberName("miter")]
    Miter,

    [JsonStringEnumMemberName("round")]
    Round,

    [JsonStringEnumMemberName("bevel")]
    Bevel
}
