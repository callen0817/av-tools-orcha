using AV.Orch.Core.Models;
using AV.Orch.Core.Services;
using AV.Orch.Security;
using AV.Orch.UnitTests.Fakes;
using Xunit;

namespace AV.Orch.UnitTests;

public sealed class SecurityAndBackupTests
{
    [Fact]
    public void SecretRedactor_RedactsOpenAiAnthropicGoogleKeysAndBearerTokens()
    {
        var redactor = new SecretRedactor();
        var raw = "OPENAI_API_KEY=sk-proj-1234567890abcdef1234567890 " +
                  "ANTHROPIC_API_KEY=sk-ant-api03-abcdef1234567890abcdef " +
                  "GEMINI_API_KEY=AIzaSyDummyKeyValue123456789012345 " +
                  "Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.payload.sig";

        var redacted = redactor.Redact(raw);

        Assert.DoesNotContain("sk-proj-", redacted);
        Assert.DoesNotContain("sk-ant-", redacted);
        Assert.DoesNotContain("AIzaSyDummy", redacted);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9", redacted);
    }

    [Theory]
    [InlineData(@"C:\Program Files\pyRevit-Master\bin\pyrevit.exe", true)]
    [InlineData(@"C:\Users\José García\Apps\pyRevit 7.1\pyrevit.exe", true)]
    [InlineData(@"\\fileserver\share\pyrevit.exe", false)]
    [InlineData("//fileserver/share/pyrevit.exe", false)]
    [InlineData(@"C:\bin\pyrevit.exe & calc.exe", false)]
    [InlineData("relative/pyrevit.exe", false)]
    public void PathValidator_EnforcesLocalSafePathsAndSupportsUnicodeAndSpaces(string path, bool expectedValid)
    {
        var result = PathValidator.ValidateLocalExecutablePath(path);
        Assert.Equal(expectedValid, result.Succeeded);
    }

    [Fact]
    public void PermissionPolicyGuard_RequiresExplicitApprovalToEnableModifications()
    {
        var blocked = PermissionPolicyGuard.ValidatePolicyTransition(
            PermissionPolicy.ReadOnly,
            PermissionPolicy.AskBeforeModifications,
            userExplicitlyApproved: false);
        Assert.False(blocked.Succeeded);
        Assert.Equal("APPROVAL_REQUIRED", blocked.ErrorCode);

        var approved = PermissionPolicyGuard.ValidatePolicyTransition(
            PermissionPolicy.ReadOnly,
            PermissionPolicy.AskBeforeModifications,
            userExplicitlyApproved: true);
        Assert.True(approved.Succeeded);

        // Switching back to ReadOnly never requires extra approval
        var stricter = PermissionPolicyGuard.ValidatePolicyTransition(
            PermissionPolicy.AskBeforeModifications,
            PermissionPolicy.ReadOnly,
            userExplicitlyApproved: false);
        Assert.True(stricter.Succeeded);
    }

    [Fact]
    public void PermissionPolicyGuard_MapsOnlyReadOnlyAndAskAndFlagsAuto()
    {
        Assert.Equal("readonly", PermissionPolicyGuard.ToPyRevitPolicyArgument(PermissionPolicy.ReadOnly));
        Assert.Equal("ask", PermissionPolicyGuard.ToPyRevitPolicyArgument(PermissionPolicy.AskBeforeModifications));
        Assert.True(PermissionPolicyGuard.IsUnsupportedAutonomousPolicy("auto"));
        Assert.Equal(PermissionPolicy.ReadOnly, PermissionPolicyGuard.FromPyRevitPolicyString("auto"));
    }

    [Fact]
    public void ConfigurationBackupService_CreatesAndRestoresVerifiedBackups()
    {
        var fs = new InMemoryFileSystem();
        var env = new FakeEnvironmentProvider();
        var backups = new ConfigurationBackupService(fs, env);

        var originalPath = "/home/testuser/.gemini/settings.json";
        const string originalContent = "{\"theme\":\"dark\",\"mcpServers\":{\"other\":{\"command\":\"npx\"}}}";
        fs.WriteAllText(originalPath, originalContent);

        var record = backups.CreateBackup(originalPath, "gemini");
        Assert.True(fs.FileExists(record.BackupPath));
        Assert.False(string.IsNullOrWhiteSpace(record.Sha256Hash));

        fs.WriteAllText(originalPath, "CORRUPTED");
        var restore = backups.RestoreBackup(record.BackupPath, originalPath);
        Assert.True(restore.Succeeded);
        Assert.Equal(originalContent, fs.ReadAllText(originalPath));
    }

    [Fact]
    public void SettingsService_PersistsSettingsAndAlwaysDisablesTelemetry()
    {
        var fs = new InMemoryFileSystem();
        var env = new FakeEnvironmentProvider();
        var service = new JsonSettingsService(fs, env);

        var defaults = service.Load();
        Assert.Equal(PermissionPolicy.ReadOnly, defaults.PermissionPolicy);
        Assert.False(defaults.TelemetryEnabled);

        service.Save(defaults with { DarkTheme = true, TelemetryEnabled = true, PermissionPolicy = PermissionPolicy.AskBeforeModifications });
        var reloaded = service.Load();
        Assert.True(reloaded.DarkTheme);
        Assert.False(reloaded.TelemetryEnabled);
        Assert.Equal(PermissionPolicy.AskBeforeModifications, reloaded.PermissionPolicy);

        var reset = service.RestoreDefaults();
        Assert.Equal(PermissionPolicy.ReadOnly, reset.PermissionPolicy);
        Assert.False(reset.DarkTheme);
    }

    [Fact]
    public void StructuredLoggerAndAuditLog_RedactSecretsBeforeWriting()
    {
        var fs = new InMemoryFileSystem();
        var env = new FakeEnvironmentProvider();
        var redactor = new SecretRedactor();
        var logger = new StructuredAppLogger(fs, env, redactor);
        var audit = new AuditLogService(fs, env, redactor);

        logger.Info("Auth", "Failed with OPENAI_API_KEY=sk-proj-secret123456789012");
        audit.Record("Connect", "User", "Codex", true, "Bearer secretToken123456789");

        var logLines = logger.ReadRecentLines();
        Assert.Single(logLines);
        Assert.DoesNotContain("sk-proj-secret123456789012", logLines[0]);

        var auditEntries = audit.GetRecentEntries();
        Assert.Single(auditEntries);
        Assert.DoesNotContain("secretToken123456789", auditEntries[0].Summary);
    }
}
