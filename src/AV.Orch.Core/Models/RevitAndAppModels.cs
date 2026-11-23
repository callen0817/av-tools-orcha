namespace AV.Orch.Core.Models;

public sealed record RevitInstallation(
    int Year,
    string DisplayName,
    string InstallDirectory,
    string ExecutablePath,
    bool IsSupported);

public sealed record RevitSession(
    int ProcessId,
    int? RevitYear,
    string? ActiveDocumentTitle,
    string? ActiveDocumentPath,
    IReadOnlyList<string> OpenDocuments,
    bool AgentHostResponding,
    bool IsBusy,
    bool IsElevated,
    string? PyRevitClone,
    string? ScriptEngine,
    string? AgentPolicy);

public sealed record PyRevitInfo(
    bool IsInstalled,
    string? ExecutablePath,
    string? Version,
    bool SupportsAgentRuntime,
    bool AgentHostEnabled,
    string? ConfiguredPolicy,
    string? DefaultEngine,
    bool RecklessModeEnabled,
    string Message);

public sealed record RevitDiagnosticResult(
    bool Succeeded,
    string Summary,
    RevitSession? ActiveSession,
    string? ActiveDocumentTitle,
    string? ErrorCode = null,
    string? RecoveryStep = null,
    TimeSpan Elapsed = default);

public sealed record DiagnosticIssue(
    string Id,
    DiagnosticCategory Category,
    DiagnosticSeverity Severity,
    string Title,
    string Description,
    string? RecoveryAction,
    bool CanAutoRepair,
    string? RepairActionId = null);

public sealed record DiagnosticReport(
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<DiagnosticIssue> Issues,
    bool IsHealthy)
{
    public int ErrorCount => Issues.Count(i => i.Severity == DiagnosticSeverity.Error);
    public int WarningCount => Issues.Count(i => i.Severity == DiagnosticSeverity.Warning);
}

public sealed record AuditEntry(
    DateTimeOffset TimestampUtc,
    string Action,
    string Actor,
    string Target,
    bool Succeeded,
    string Summary);

public sealed record AppSettings
{
    public PermissionPolicy PermissionPolicy { get; init; } = PermissionPolicy.ReadOnly;
    public bool LaunchAtStartup { get; init; }
    public bool CheckForUpdates { get; init; } = true;
    public bool DarkTheme { get; init; }
    public bool TelemetryEnabled { get; init; } // Always false by default per security policy
    public bool ModelDataNoticeAcknowledged { get; init; }
    public int LogRetentionDays { get; init; } = 14;
    public int PreferredRevitYear { get; init; }
    public string? CustomPyRevitPath { get; init; }
}
