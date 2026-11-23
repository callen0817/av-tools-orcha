using AV.Orch.Core.Abstractions;
using AV.Orch.Core.Models;
using AV.Orch.Core.Services;
using AV.Orch.Installation;
using AV.Orch.Providers.Claude;
using AV.Orch.Providers.Codex;
using AV.Orch.Providers.Gemini;
using AV.Orch.Revit;
using AV.Orch.Security;
using AV.Orch.UnitTests.Fakes;
using Xunit;

namespace AV.Orch.UnitTests;

public sealed class RevitAndDiagnosticsTests
{
    [Fact]
    public async Task RevitDiscovery_SupportsRevit2023ForwardOnly()
    {
        var fs = new InMemoryFileSystem();
        var env = new FakeEnvironmentProvider { ProgramFilesDirectory = "/ProgramFiles" };

        fs.WriteAllText("/ProgramFiles/Autodesk/Revit 2022/Revit.exe", "bin");
        fs.WriteAllText("/ProgramFiles/Autodesk/Revit 2023/Revit.exe", "bin");
        fs.WriteAllText("/ProgramFiles/Autodesk/Revit 2025/Revit.exe", "bin");
        fs.WriteAllText("/ProgramFiles/Autodesk/Revit 2028/Revit.exe", "bin");

        var discovery = new RevitDiscoveryService(fs, env);
        var installs = await discovery.DiscoverInstallationsAsync();

        Assert.Equal(4, installs.Count);
        Assert.False(installs.First(r => r.Year == 2022).IsSupported);
        Assert.True(installs.First(r => r.Year == 2023).IsSupported);
        Assert.True(installs.First(r => r.Year == 2025).IsSupported);
        Assert.True(installs.First(r => r.Year == 2028).IsSupported);
    }

    [Fact]
    public async Task PyRevitService_DetectsAgentRuntime_ParsesSessions_AndRunsReadOnlyContextQuery()
    {
        var fs = new InMemoryFileSystem();
        var env = new FakeEnvironmentProvider();
        var proc = new FakeProcessRunner();
        proc.PathExecutables["pyrevit"] = @"C:\Program Files\pyRevit-Master\bin\pyrevit.exe";
        proc.RunHandler = (_, args) =>
        {
            var joined = string.Join(" ", args);
            return joined switch
            {
                "--version" => new ProcessExecutionResult(0, "pyRevit CLI v7.1.0", string.Empty),
                "configs agent" => new ProcessExecutionResult(0, "agent enabled = true", string.Empty),
                "configs agent policy" => new ProcessExecutionResult(0, "policy = readonly", string.Empty),
                "configs agent engine" => new ProcessExecutionResult(0, "engine = ironpython", string.Empty),
                "configs agent reckless" => new ProcessExecutionResult(0, "reckless = false", string.Empty),
                "agent status --json" => new ProcessExecutionResult(0, """[{"pid":4321,"revit_year":2025,"responding":true,"busy":false,"document":"SampleOffice.rvt","clone":"master","engine":"ironpython","policy":"readonly"}]""", string.Empty),
                "agent context" => new ProcessExecutionResult(0, """{"revit":{"year":2025},"document":{"title":"SampleOffice.rvt","path":"C:\\Projects\\SampleOffice.rvt"},"open_documents":["SampleOffice.rvt"],"agent":{"policy":"readonly"},"scripting":{"default_engine":"ironpython"}}""", string.Empty),
                _ => new ProcessExecutionResult(0, string.Empty, string.Empty)
            };
        };

        var logger = new StructuredAppLogger(fs, env, new SecretRedactor());
        var service = new PyRevitService(proc, fs, env, logger);

        var info = await service.DetectPyRevitAsync();
        Assert.True(info.IsInstalled);
        Assert.True(info.SupportsAgentRuntime);
        Assert.True(info.AgentHostEnabled);
        Assert.Equal("readonly", info.ConfiguredPolicy);

        var sessions = await service.GetRunningSessionsAsync(info.ExecutablePath!);
        Assert.Single(sessions);
        Assert.Equal(4321, sessions[0].ProcessId);
        Assert.Equal("SampleOffice.rvt", sessions[0].ActiveDocumentTitle);

        var diag = await service.RunReadOnlyDiagnosticQueryAsync(info.ExecutablePath!);
        Assert.True(diag.Succeeded);
        Assert.Equal("SampleOffice.rvt", diag.ActiveDocumentTitle);
    }

    [Fact]
    public async Task PyRevitService_HandlesTimeoutAndUnsupportedVersion()
    {
        var fs = new InMemoryFileSystem();
        var env = new FakeEnvironmentProvider();
        var proc = new FakeProcessRunner();
        proc.PathExecutables["pyrevit"] = @"C:\pyRevit\pyrevit.exe";
        proc.RunHandler = (_, args) =>
        {
            var joined = string.Join(" ", args);
            if (joined == "--version") return new ProcessExecutionResult(0, "pyRevit CLI v4.8.16", string.Empty);
            if (joined == "agent status --json") return new ProcessExecutionResult(0, """[{"pid":100,"revit_year":2024,"responding":true}]""", string.Empty);
            if (joined == "agent context") return new ProcessExecutionResult(-1, string.Empty, string.Empty, TimedOut: true);
            return new ProcessExecutionResult(0, string.Empty, string.Empty);
        };

        var service = new PyRevitService(proc, fs, env, new StructuredAppLogger(fs, env, new SecretRedactor()));
        var info = await service.DetectPyRevitAsync();
        Assert.True(info.IsInstalled);
        Assert.False(info.SupportsAgentRuntime);

        var timeoutDiag = await service.RunReadOnlyDiagnosticQueryAsync(@"C:\pyRevit\pyrevit.exe");
        Assert.False(timeoutDiag.Succeeded);
        Assert.Equal("CONNECTION_TIMEOUT", timeoutDiag.ErrorCode);
    }

    [Fact]
    public async Task DiagnosticsService_ReportsMissingPrereqsElevatedAndDisabledRuntime()
    {
        var env = new FakeEnvironmentProvider { IsElevated = true };
        var diagService = new DiagnosticsService(env);

        var summary = new DependencyDiscoverySummary(
            Array.Empty<RevitInstallation>(),
            new PyRevitInfo(true, @"C:\pyrevit.exe", "7.1.0", true, false, "readonly", "ironpython", false, "Disabled"),
            Array.Empty<ProviderSnapshot>(),
            DateTimeOffset.UtcNow);

        var report = await diagService.RunDiagnosticsAsync(summary, Array.Empty<RevitSession>());
        Assert.False(report.IsHealthy);
        Assert.Contains(report.Issues, i => i.Id == "ENV_ELEVATED");
        Assert.Contains(report.Issues, i => i.Id == "REVIT_NOT_FOUND");
        Assert.Contains(report.Issues, i => i.Id == "PYREVIT_AGENT_DISABLED" && i.CanAutoRepair);
        Assert.Contains(report.Issues, i => i.Id == "REVIT_NOT_RUNNING");
    }

    [Fact]
    public async Task DependencyInstallWorkflow_RequiresExplicitUserApproval()
    {
        var fs = new InMemoryFileSystem();
        var env = new FakeEnvironmentProvider();
        var proc = new FakeProcessRunner();
        var audit = new AuditLogService(fs, env, new SecretRedactor());
        var workflow = new DependencyInstallWorkflow(proc, audit);

        var geminiDesc = DependencyInstallWorkflow.GetOfficialDescriptors().First(d => d.Id == "gemini");
        var blocked = await workflow.InstallNpmCliWithApprovalAsync(geminiDesc, userApproved: false);
        Assert.False(blocked.Succeeded);
        Assert.Equal("APPROVAL_REQUIRED", blocked.ErrorCode);
        Assert.Empty(proc.Invocations);
    }
}
