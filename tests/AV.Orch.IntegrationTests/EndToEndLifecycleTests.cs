using AV.Orch.Core.Abstractions;
using AV.Orch.Core.Models;
using AV.Orch.Core.Services;
using AV.Orch.Installation;
using AV.Orch.Providers.Claude;
using AV.Orch.Providers.Codex;
using AV.Orch.Providers.Gemini;
using AV.Orch.Revit;
using AV.Orch.Security;
using Xunit;

namespace AV.Orch.IntegrationTests;

public sealed class EndToEndLifecycleTests : IDisposable
{
    private readonly string _tempRoot;

    public EndToEndLifecycleTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"av-orch-it-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    [Fact]
    public async Task AllThreeProviders_CoexistOnRealDisk_PreserveUnrelatedServers_AndCleanlyUninstall()
    {
        var env = new TempEnvironmentProvider(_tempRoot);
        var fs = new LocalFileSystem();
        var redactor = new SecretRedactor();
        var logger = new StructuredAppLogger(fs, env, redactor);
        var audit = new AuditLogService(fs, env, redactor);
        var settings = new JsonSettingsService(fs, env);
        var backups = new ConfigurationBackupService(fs, env);
        var probe = new AlwaysOkProbe();

        var pyRevitExe = Path.Combine(_tempRoot, "ProgramFiles", "pyRevit-Master", "bin", "pyrevit.exe");
        fs.WriteAllText(pyRevitExe, "stub");
        var revitExe = Path.Combine(_tempRoot, "ProgramFiles", "Autodesk", "Revit 2025", "Revit.exe");
        fs.WriteAllText(revitExe, "stub");

        // Pre-populate existing user configs with unrelated MCP servers
        fs.WriteAllText(Path.Combine(env.UserProfileDirectory, ".claude.json"),
            """{"mcpServers":{"existing-claude":{"type":"stdio","command":"npx","args":["foo"]}}}""");
        fs.WriteAllText(Path.Combine(env.UserProfileDirectory, ".gemini", "settings.json"),
            """{"mcpServers":{"existing-gemini":{"command":"npx","args":["bar"]}}}""");
        fs.WriteAllText(Path.Combine(env.UserProfileDirectory, ".codex", "config.toml"),
            """
            # Existing Codex config
            [mcp_servers.existing_codex]
            command = "npx"
            args = ["baz"]
            """);

        var proc = new StubCliProcessRunner(pyRevitExe);
        var revitDiscovery = new RevitDiscoveryService(fs, env);
        var pyRevitService = new PyRevitService(proc, fs, env, logger);
        var diagnostics = new DiagnosticsService(env);

        var codex = new CodexAdapter(proc, fs, env, backups, probe, logger);
        var gemini = new GeminiAdapter(proc, fs, env, backups, probe, logger);
        var claude = new ClaudeCodeAdapter(proc, fs, env, backups, probe, logger);
        IAiProviderAdapter[] adapters = { codex, gemini, claude };

        var discovery = new DependencyDiscoveryService(revitDiscovery, pyRevitService, adapters);
        var orchestrator = new AvOrchOrchestrator(discovery, pyRevitService, diagnostics, settings, audit, logger, adapters);
        var uninstallCleanup = new UninstallCleanupService(adapters, audit, logger);

        // Connect all 3 providers with explicit approval
        Assert.True((await orchestrator.ConnectProviderAsync(ProviderId.Codex, true)).Succeeded);
        Assert.True((await orchestrator.ConnectProviderAsync(ProviderId.Gemini, true)).Succeeded);
        Assert.True((await orchestrator.ConnectProviderAsync(ProviderId.ClaudeCode, true)).Succeeded);

        // Verify all 3 coexist & preserve existing servers
        var (summary, sessions, report) = await orchestrator.RefreshAllAsync();
        Assert.Single(summary.RevitInstallations);
        Assert.True(summary.PyRevit.SupportsAgentRuntime);
        Assert.Single(sessions);
        Assert.True(report.IsHealthy);

        foreach (var p in summary.Providers)
        {
            Assert.Equal(RegistrationStatus.Registered, p.Registration.Status);
            Assert.True(p.Registration.OwnedByAvOrch);
            Assert.Equal(1, p.Registration.OtherServerCount);
        }

        // Verify Read-Only test succeeds
        var revitTest = await orchestrator.TestRevitReadOnlyConnectionAsync();
        Assert.True(revitTest.Succeeded);
        Assert.Equal("TowerA.rvt", revitTest.ActiveDocumentTitle);

        // Verify permission transition requires explicit approval
        var denied = await orchestrator.SetPermissionPolicyAsync(PermissionPolicy.AskBeforeModifications, userExplicitlyApproved: false);
        Assert.False(denied.Succeeded);
        var approved = await orchestrator.SetPermissionPolicyAsync(PermissionPolicy.AskBeforeModifications, userExplicitlyApproved: true);
        Assert.True(approved.Succeeded);

        // Clean uninstall removes ONLY AV Orch entries, leaving existing servers intact
        var cleanup = await uninstallCleanup.RemoveAllAvOrchRegistrationsAsync();
        Assert.True(cleanup.Succeeded);
        Assert.Equal(3, cleanup.CleanedProviders.Count);

        Assert.Contains("existing-claude", fs.ReadAllText(claude.GetUserConfigPath()));
        Assert.DoesNotContain("\"pyrevit\"", fs.ReadAllText(claude.GetUserConfigPath()));

        Assert.Contains("existing-gemini", fs.ReadAllText(gemini.GetUserConfigPath()));
        Assert.DoesNotContain("\"pyrevit\"", fs.ReadAllText(gemini.GetUserConfigPath()));

        Assert.Contains("[mcp_servers.existing_codex]", fs.ReadAllText(codex.GetUserConfigPath()));
        Assert.DoesNotContain("[mcp_servers.pyrevit]", fs.ReadAllText(codex.GetUserConfigPath()));
    }

    [Fact]
    public async Task McpStdioProbe_ValidatesRealJsonRpcSubprocessOverStdio()
    {
        var scriptPath = Path.Combine(_tempRoot, "mock_mcp_server.py");
        File.WriteAllText(scriptPath, """
import sys, json
for line in sys.stdin:
    msg = json.loads(line)
    method = msg.get("method")
    if method == "initialize":
        resp = {"jsonrpc":"2.0","id":msg["id"],"result":{"protocolVersion":"2024-11-05","capabilities":{"tools":{}},"serverInfo":{"name":"pyrevit-mock","version":"7.1.0"}}}
        sys.stdout.write(json.dumps(resp) + "\n")
        sys.stdout.flush()
    elif method == "tools/list":
        resp = {"jsonrpc":"2.0","id":msg["id"],"result":{"tools":[{"name":"get_context"},{"name":"list_revit_instances"}]}}
        sys.stdout.write(json.dumps(resp) + "\n")
        sys.stdout.flush()
""");

        var runner = new SystemProcessRunner();
        var pythonExe = await runner.FindExecutableInPathAsync("python3") ?? await runner.FindExecutableInPathAsync("python");
        Assert.False(string.IsNullOrWhiteSpace(pythonExe), "python3/python must be available to run stdio MCP probe integration test.");

        var probe = new McpStdioProbe();
        var res = await probe.ProbeInitializeAndToolsAsync(pythonExe!, new[] { scriptPath }, TimeSpan.FromSeconds(10));
        Assert.True(res.Succeeded, res.Message);
    }

    private sealed class TempEnvironmentProvider : IEnvironmentProvider
    {
        public string UserProfileDirectory { get; }
        public string LocalAppDataDirectory { get; }
        public string RoamingAppDataDirectory { get; }
        public string ProgramFilesDirectory { get; }
        public bool IsWindows => false;
        public bool IsElevated => false;
        public string? GetEnvironmentVariable(string name) => null;

        public TempEnvironmentProvider(string root)
        {
            UserProfileDirectory = Path.Combine(root, "UserProfile");
            LocalAppDataDirectory = Path.Combine(root, "LocalAppData");
            RoamingAppDataDirectory = Path.Combine(root, "RoamingAppData");
            ProgramFilesDirectory = Path.Combine(root, "ProgramFiles");
            Directory.CreateDirectory(UserProfileDirectory);
            Directory.CreateDirectory(LocalAppDataDirectory);
            Directory.CreateDirectory(RoamingAppDataDirectory);
            Directory.CreateDirectory(ProgramFilesDirectory);
        }
    }

    private sealed class AlwaysOkProbe : IMcpStdioProbe
    {
        public Task<OperationResult> ProbeInitializeAndToolsAsync(
            string command, IReadOnlyList<string> arguments, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Ok("OK"));
    }

    private sealed class StubCliProcessRunner : IProcessRunner
    {
        private readonly string _pyRevitExe;

        public StubCliProcessRunner(string pyRevitExe) => _pyRevitExe = pyRevitExe;

        public Task<ProcessExecutionResult> RunAsync(
            string fileName, IReadOnlyList<string> arguments, string? workingDirectory = null, TimeSpan? timeout = null,
            IReadOnlyDictionary<string, string>? environmentVariables = null, CancellationToken cancellationToken = default)
        {
            var joined = string.Join(" ", arguments);
            if (fileName.Contains("pyrevit", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(joined switch
                {
                    "--version" => new ProcessExecutionResult(0, "pyRevit CLI v7.1.0", string.Empty),
                    "configs agent" => new ProcessExecutionResult(0, "true", string.Empty),
                    "configs agent policy" => new ProcessExecutionResult(0, "readonly", string.Empty),
                    "configs agent engine" => new ProcessExecutionResult(0, "ironpython", string.Empty),
                    "configs agent reckless" => new ProcessExecutionResult(0, "false", string.Empty),
                    "agent status --json" => new ProcessExecutionResult(0, """[{"pid":9001,"revit_year":2025,"responding":true,"document":"TowerA.rvt","policy":"readonly"}]""", string.Empty),
                    "agent context" => new ProcessExecutionResult(0, """{"revit":{"year":2025},"document":{"title":"TowerA.rvt"},"agent":{"policy":"readonly"}}""", string.Empty),
                    "mcp install claude" or "mcp install codex" => new ProcessExecutionResult(1, string.Empty, "use surgical config"),
                    _ => new ProcessExecutionResult(0, "ok", string.Empty)
                });
            }

            return Task.FromResult(joined switch
            {
                "--version" => new ProcessExecutionResult(0, "1.0.0", string.Empty),
                "auth status" or "login status" => new ProcessExecutionResult(0, "logged in", string.Empty),
                _ => new ProcessExecutionResult(0, string.Empty, string.Empty)
            });
        }

        public Task<OperationResult> LaunchInteractiveAsync(
            string fileName, IReadOnlyList<string> arguments, string? workingDirectory = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Ok("Launched"));

        public Task<string?> FindExecutableInPathAsync(string executableName, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(executableName.ToLowerInvariant() switch
            {
                "pyrevit" => _pyRevitExe,
                "codex" => "/usr/local/bin/codex",
                "gemini" => "/usr/local/bin/gemini",
                "claude" => "/usr/local/bin/claude",
                _ => null
            });
    }
}
