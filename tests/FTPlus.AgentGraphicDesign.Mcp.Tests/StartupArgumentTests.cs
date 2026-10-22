using System.Diagnostics;
using Xunit;
using McpProgram = FTPlus.AgentGraphicDesign.Mcp.Program;

namespace FTPlus.AgentGraphicDesign.Mcp.Tests;

public class StartupArgumentTests
{
    private static readonly string McpAssemblyPath = typeof(McpProgram).Assembly.Location;

    [Theory]
    [InlineData("--unknown")]
    [InlineData("--working-directory")]
    [InlineData("--working-directory", "--version")]
    [InlineData("--working-directory", ".", "-w", ".")]
    [InlineData("--help", "--version")]
    public async Task MalformedStartupArguments_ReturnNonzeroUsageExit(params string[] args)
    {
        var result = await RunAsync(args);

        Assert.Equal(McpProgram.UsageExitCode, result.ExitCode);
        Assert.NotEmpty(result.Error);
    }

    [Fact]
    public async Task Help_UsesActualSnakeCaseToolNames()
    {
        var result = await RunAsync("--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("create_document", result.Output);
        Assert.Contains("import_dtcg_tokens", result.Output);
        Assert.Contains("render_preview", result.Output);
        Assert.Contains("preflight_artifact", result.Output);
        Assert.Contains("verify_artifact", result.Output);
        Assert.DoesNotContain("CreateDocument", result.Output);
        Assert.DoesNotContain("PreflightArtifact", result.Output);
        Assert.DoesNotContain("VerifyArtifact", result.Output);
    }

    [Fact]
    public async Task Help_ListsReadOnlyResourceUris()
    {
        var result = await RunAsync("--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("AVAILABLE RESOURCES", result.Output);
        Assert.Contains("agd://schema", result.Output);
        Assert.Contains("agd://format", result.Output);
        Assert.Contains("agd://agent-guide", result.Output);
        Assert.Contains("text/markdown", result.Output);
    }

    [Fact]
    public void NoWorkingDirectoryOption_DefaultsOnlyThen()
    {
        var defaults = McpProgram.ParseArguments([]);
        var explicitDirectory = McpProgram.ParseArguments(["--working-directory", "repository"]);

        Assert.True(defaults.Success);
        Assert.Null(defaults.WorkingDirectory);
        Assert.Equal("repository", explicitDirectory.WorkingDirectory);
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(params string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(McpAssemblyPath);
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, output, error);
    }
}
