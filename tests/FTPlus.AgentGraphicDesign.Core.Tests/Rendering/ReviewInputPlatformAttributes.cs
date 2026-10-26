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

public sealed class LinuxInspectionFactAttribute : FactAttribute
{
    public LinuxInspectionFactAttribute()
    {
        if (!OperatingSystem.IsLinux() || !PlatformProfile.Current.GateAdmits(PlatformCapability.AssetPipeline))
            Skip = "Requires Linux with admitted asset-pipeline inspection";
    }
}

public sealed class LinuxInspectionTheoryAttribute : TheoryAttribute
{
    public LinuxInspectionTheoryAttribute()
    {
        if (!OperatingSystem.IsLinux() || !PlatformProfile.Current.GateAdmits(PlatformCapability.AssetPipeline))
            Skip = "Requires Linux with admitted asset-pipeline inspection";
    }
}
