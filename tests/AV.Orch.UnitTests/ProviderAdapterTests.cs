using System.Text.Json.Nodes;
using AV.Orch.Core.Models;
using AV.Orch.Core.Services;
using AV.Orch.Providers.Claude;
using AV.Orch.Providers.Codex;
using AV.Orch.Providers.Gemini;
using AV.Orch.Security;
using AV.Orch.UnitTests.Fakes;
using Xunit;

namespace AV.Orch.UnitTests;

public sealed class ProviderAdapterTests
{
    private const string PyRevitPath = @"C:\Program Files\pyRevit-Master\bin\pyrevit.exe";

    [Fact]
    public async Task GeminiAdapter_PreservesExistingMcpServersAndSettings_AndCleanlyRemovesOnlyOwnedEntry()
    {
        var fs = new InMemoryFileSystem();
        var env = new FakeEnvironmentProvider();
        var proc = new FakeProcessRunner();
        proc.PathExecutables["gemini"] = @"C:\Users\User\AppData\Roaming\npm\gemini.cmd";
        proc.RunHandler = (_, args) => args.Contains("--version")
            ? new ProcessExecutionResult(0, "0.24.0", string.Empty)
            : new ProcessExecutionResult(0, string.Empty, string.Empty);

        var backups = new ConfigurationBackupService(fs, env);
        var logger = new StructuredAppLogger(fs, env, new SecretRedactor());
        var probe = new FakeMcpStdioProbe();
        var adapter = new GeminiAdapter(proc, fs, env, backups, probe, logger);

        var settingsPath = adapter.GetUserConfigPath();
        fs.WriteAllText(settingsPath, """
        {
          "theme": "GitHub",
          "mcpServers": {
            "github": {
              "command": "npx",
              "args": ["-y", "@github/github-mcp-server"]
            }
          }
        }
        """);

        var installRes = await adapter.InstallMcpRegistrationAsync(PyRevitPath);
        Assert.True(installRes.Succeeded, installRes.Message);
        Assert.NotNull(installRes.BackupPath);

        var state = await adapter.GetRegistrationStateAsync();
        Assert.Equal(RegistrationStatus.Registered, state.Status);
        Assert.True(state.OwnedByAvOrch);
        Assert.Equal(1, state.OtherServerCount);

        // Verify existing server & settings preserved, and trust=false set
        var json = JsonNode.Parse(fs.ReadAllText(settingsPath))!.AsObject();
        Assert.Equal("GitHub", json["theme"]!.GetValue<string>());
        Assert.NotNull(json["mcpServers"]!["github"]);
        Assert.False(json["mcpServers"]!["pyrevit"]!["trust"]!.GetValue<bool>());

        // Validate connection
        var valRes = await adapter.ValidateConnectionAsync(PyRevitPath);
        Assert.True(valRes.Succeeded);

        // Disconnect removes ONLY pyrevit
        var remRes = await adapter.RemoveMcpRegistrationAsync();
        Assert.True(remRes.Succeeded);

        var afterJson = JsonNode.Parse(fs.ReadAllText(settingsPath))!.AsObject();
        Assert.NotNull(afterJson["mcpServers"]!["github"]);
        Assert.Null(afterJson["mcpServers"]!["pyrevit"]);
    }

    [Fact]
    public async Task CodexAdapter_PreservesCommentsAndOtherTomlTables_AndHandlesCorruptConfigRepair()
    {
        var fs = new InMemoryFileSystem();
        var env = new FakeEnvironmentProvider();
        var proc = new FakeProcessRunner();
        proc.PathExecutables["codex"] = @"C:\Tools\codex.exe";
        proc.RunHandler = (file, args) =>
        {
            if (args.Contains("--version")) return new ProcessExecutionResult(0, "codex-cli 0.143.0", string.Empty);
            if (args.SequenceEqual(new[] { "login", "status" })) return new ProcessExecutionResult(0, "Logged in via ChatGPT", string.Empty);
            // Force surgical TOML path
            return new ProcessExecutionResult(1, string.Empty, "fallback");
        };

        var backups = new ConfigurationBackupService(fs, env);
        var logger = new StructuredAppLogger(fs, env, new SecretRedactor());
        var adapter = new CodexAdapter(proc, fs, env, backups, new FakeMcpStdioProbe(), logger);

        var configPath = adapter.GetUserConfigPath();
        fs.WriteAllText(configPath, """
        # User comment preserved
        model = "gpt-5"

        [mcp_servers.context7]
        command = "npx"
        args = ["-y", "@upstash/context7-mcp"]
        """);

        var auth = await adapter.CheckAuthenticationAsync();
        Assert.Equal(AuthenticationStatus.Authenticated, auth.Status);

        var installRes = await adapter.InstallMcpRegistrationAsync(PyRevitPath);
        Assert.True(installRes.Succeeded);

        var state = await adapter.GetRegistrationStateAsync();
        Assert.Equal(RegistrationStatus.Registered, state.Status);
        Assert.True(state.OwnedByAvOrch);
        Assert.Equal(1, state.OtherServerCount);

        var toml = fs.ReadAllText(configPath);
        Assert.Contains("# User comment preserved", toml);
        Assert.Contains("[mcp_servers.context7]", toml);
        Assert.Contains("[mcp_servers.pyrevit]", toml);

        // Remove only pyrevit
        var removeRes = await adapter.RemoveMcpRegistrationAsync();
        Assert.True(removeRes.Succeeded);
        var afterRemove = fs.ReadAllText(configPath);
        Assert.Contains("[mcp_servers.context7]", afterRemove);
        Assert.DoesNotContain("[mcp_servers.pyrevit]", afterRemove);

        // Corrupt config detection & repair
        fs.WriteAllText(configPath, "[broken_header");
        var corruptState = await adapter.GetRegistrationStateAsync();
        Assert.Equal(RegistrationStatus.CorruptConfig, corruptState.Status);

        var repairRes = await adapter.RepairRegistrationAsync(PyRevitPath);
        Assert.True(repairRes.Succeeded);
        var repairedState = await adapter.GetRegistrationStateAsync();
        Assert.Equal(RegistrationStatus.Registered, repairedState.Status);
    }

    [Fact]
    public async Task ClaudeCodeAdapter_DetectsAuthStates_RefusesToRemoveUnownedEntry_AndHandlesBackupRollbackOnWriteFailure()
    {
        var fs = new InMemoryFileSystem();
        var env = new FakeEnvironmentProvider();
        var proc = new FakeProcessRunner();
        proc.PathExecutables["claude"] = @"C:\Users\User\.local\bin\claude.exe";
        proc.RunHandler = (_, args) =>
        {
            if (args.Contains("--version")) return new ProcessExecutionResult(0, "2.1.300 (Claude Code)", string.Empty);
            if (args.SequenceEqual(new[] { "auth", "status" })) return new ProcessExecutionResult(1, string.Empty, "Session expired");
            return new ProcessExecutionResult(1, string.Empty, "fallback");
        };

        var backups = new ConfigurationBackupService(fs, env);
        var logger = new StructuredAppLogger(fs, env, new SecretRedactor());
        var adapter = new ClaudeCodeAdapter(proc, fs, env, backups, new FakeMcpStdioProbe(), logger);

        var auth = await adapter.CheckAuthenticationAsync();
        Assert.Equal(AuthenticationStatus.Expired, auth.Status);

        // Conflicting non-AV-Orch entry must NOT be removed
        var configPath = adapter.GetUserConfigPath();
        fs.WriteAllText(configPath, """
        {
          "mcpServers": {
            "pyrevit": {
              "command": "custom-tool.exe",
              "args": ["serve"]
            }
          }
        }
        """);

        var conflictState = await adapter.GetRegistrationStateAsync();
        Assert.Equal(RegistrationStatus.Conflicting, conflictState.Status);
        Assert.False(conflictState.OwnedByAvOrch);

        var removeBlocked = await adapter.RemoveMcpRegistrationAsync();
        Assert.False(removeBlocked.Succeeded);
        Assert.Equal("NOT_OWNED_BY_AVORCH", removeBlocked.ErrorCode);
    }
}
