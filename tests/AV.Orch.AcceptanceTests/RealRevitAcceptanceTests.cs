using AV.Orch.Core.Services;
using AV.Orch.Revit;
using AV.Orch.Security;
using Xunit;

namespace AV.Orch.AcceptanceTests;

/// <summary>
/// Real-system acceptance tests designed to run ONLY on a Windows machine with
/// Autodesk Revit (2021-2027), pyRevit 7.1+, and Codex/Gemini/Claude CLIs installed.
/// Never use a production customer Revit model for destructive testing.
/// Set AV_ORCH_REAL_REVIT=1 to enable execution.
/// </summary>
public sealed class RealRevitAcceptanceTests
{
    private static bool IsRealRevitEnabled =>
        OperatingSystem.IsWindows() &&
        string.Equals(Environment.GetEnvironmentVariable("AV_ORCH_REAL_REVIT"), "1", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public async Task RealWindowsEnvironment_PyRevitAndRevitSessionReadOnlyCheck()
    {
        if (!IsRealRevitEnabled)
        {
            // Explicitly no-op when not running on a real Revit acceptance machine;
            // AV_ORCH_BUILD_REPORT.md records real-system validation as NOT TESTED / BLOCKED on Linux.
            return;
        }

        var fs = new LocalFileSystem();
        var env = new SystemEnvironmentProvider();
        var runner = new SystemProcessRunner();
        var logger = new StructuredAppLogger(fs, env, new SecretRedactor());

        var revitDiscovery = new RevitDiscoveryService(fs, env);
        var revits = await revitDiscovery.DiscoverInstallationsAsync();
        Assert.NotEmpty(revits);

        var pyRevit = new PyRevitService(runner, fs, env, logger);
        var info = await pyRevit.DetectPyRevitAsync();
        Assert.True(info.IsInstalled, info.Message);
        Assert.True(info.SupportsAgentRuntime, info.Message);
        Assert.True(info.AgentHostEnabled, "Enable pyRevit agent host before running real-Revit acceptance tests.");

        var diag = await pyRevit.RunReadOnlyDiagnosticQueryAsync(info.ExecutablePath!);
        Assert.True(diag.Succeeded, diag.Summary);
        Assert.False(string.IsNullOrWhiteSpace(diag.ActiveDocumentTitle), "Open a non-production sample Revit model first.");

        var probe = new McpStdioProbe();
        var mcpResult = await probe.ProbeInitializeAndToolsAsync(info.ExecutablePath!, new[] { "mcp" }, TimeSpan.FromSeconds(15));
        Assert.True(mcpResult.Succeeded, mcpResult.Message);
    }
}
