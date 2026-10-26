using System.Reflection;
using System.Text.RegularExpressions;
using FTPlus.AgentGraphicDesign.Core.Platform;
using FTPlus.AgentGraphicDesign.Core.Tests.Schema;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Platform;

public sealed class PlatformArchitectureTests
{
    private static readonly string[] RegistrySources = ["PlatformProfile.cs", "PlatformCapability.cs", "PlatformAvailabilityContract.cs"];
    private static readonly string[] BannedTokens =
    [
        "System.IO", "File.", "File(", "Directory.", "DriveInfo", "FileInfo", "DirectoryInfo", "FileStream",
        "StreamReader", "Path.", "DllImport", "LibraryImport", "extern ", "NativeLibrary", "Process.",
        "GetEnvironmentVariable", "qualification-evidence", "AppContext.BaseDirectory", "HttpClient", "Socket"
    ];
    private static string SourceDirectory => Path.Combine(JsonSchemaContractTests.FindRepositoryRoot(), "src", "FTPlus.AgentGraphicDesign.Core", "Platform");
    [Theory]
    [InlineData("PlatformProfile.cs")]
    [InlineData("PlatformCapability.cs")]
    [InlineData("PlatformAvailabilityContract.cs")]
    public void RegistrySource_ReferencesNoIoApi(string file)
    {
        var code = string.Join('\n', File.ReadAllLines(Path.Combine(SourceDirectory, file)).Select(l => Regex.Replace(l, @"//.*$", "")));
        foreach (var token in BannedTokens) Assert.False(code.Contains(token, StringComparison.Ordinal), $"{file} references '{token}'");
    }
    [Fact]
    public void PlatformDirectory_ContainsOnlyTheScannedRegistrySources() => Assert.Equal(RegistrySources.Order(StringComparer.Ordinal),
        Directory.EnumerateFiles(SourceDirectory, "*.cs").Select(Path.GetFileName).Order(StringComparer.Ordinal));
    [Fact]
    public void PlatformNamespace_DeclaresNoPInvoke() => Assert.DoesNotContain(typeof(PlatformProfile).Assembly.GetTypes()
        .Where(t => t.Namespace == typeof(PlatformProfile).Namespace)
        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)),
        m => (m.Attributes & MethodAttributes.PinvokeImpl) != 0);
}
