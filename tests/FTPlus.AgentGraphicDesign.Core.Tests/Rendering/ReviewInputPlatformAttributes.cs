using FTPlus.AgentGraphicDesign.Core.Platform;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Rendering;

public sealed class InspectionFactAttribute : FactAttribute
{
    public InspectionFactAttribute()
    {
        if (!PlatformProfile.Current.GateAdmits(PlatformCapability.AssetPipeline))
            Skip = "Requires admitted asset-pipeline inspection";
    }
}

public sealed class UnixInspectionFactAttribute : FactAttribute
{
    public UnixInspectionFactAttribute()
    {
        if (!(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) || !PlatformProfile.Current.GateAdmits(PlatformCapability.AssetPipeline))
            Skip = "Requires Unix with admitted asset-pipeline inspection";
    }
}

public sealed class UnixInspectionTheoryAttribute : TheoryAttribute
{
    public UnixInspectionTheoryAttribute()
    {
        if (!(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) || !PlatformProfile.Current.GateAdmits(PlatformCapability.AssetPipeline))
            Skip = "Requires Unix with admitted asset-pipeline inspection";
    }
}
