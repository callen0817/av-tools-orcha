using AV.Orch.Core.Abstractions;
using AV.Orch.Core.Models;

namespace AV.Orch.Core.Services;

public sealed class DiagnosticsService : IDiagnosticsService
{
    private readonly IEnvironmentProvider _environment;

    public DiagnosticsService(IEnvironmentProvider environment)
    {
        _environment = environment;
    }

    public Task<DiagnosticReport> RunDiagnosticsAsync(
        DependencyDiscoverySummary discovery,
        IReadOnlyList<RevitSession> sessions,
        CancellationToken cancellationToken = default)
    {
        var issues = new List<DiagnosticIssue>();

        if (_environment.IsElevated)
        {
            issues.Add(new DiagnosticIssue(
                "ENV_ELEVATED",
                DiagnosticCategory.PyRevitRuntime,
                DiagnosticSeverity.Warning,
                "Running as Administrator",
                "pyRevit's agent host never starts in an elevated Revit session, and elevated registration skips Claude/Codex CLI commands.",
                "Restart AV Orch and Autodesk Revit as a standard (non-administrator) user.",
                CanAutoRepair: false));
        }

        if (discovery.RevitInstallations.Count == 0)
        {
            issues.Add(new DiagnosticIssue(
                "REVIT_NOT_FOUND",
                DiagnosticCategory.Prerequisite,
                DiagnosticSeverity.Error,
                "Autodesk Revit Not Detected",
                "No supported Autodesk Revit (2023 or newer) installation was found on this machine.",
                "Install Autodesk Revit 2023 or newer before connecting AI providers.",
                CanAutoRepair: false));
        }
        else if (discovery.RevitInstallations.All(r => !r.IsSupported))
        {
            issues.Add(new DiagnosticIssue(
                "REVIT_UNSUPPORTED",
                DiagnosticCategory.VersionCompatibility,
                DiagnosticSeverity.Error,
                "Unsupported Revit Version",
                "Installed Revit versions are older than the AV Orch minimum supported version (Autodesk Revit 2023).",
                "Upgrade to Autodesk Revit 2023 or newer.",
                CanAutoRepair: false));
        }

        if (!discovery.PyRevit.IsInstalled)
        {
            issues.Add(new DiagnosticIssue(
                "PYREVIT_NOT_FOUND",
                DiagnosticCategory.Prerequisite,
                DiagnosticSeverity.Error,
                "pyRevit Not Detected",
                discovery.PyRevit.Message,
                "Install official pyRevit 7.1+ from https://github.com/pyrevitlabs/pyRevit/releases.",
                CanAutoRepair: false));
        }
        else if (!discovery.PyRevit.SupportsAgentRuntime)
        {
            issues.Add(new DiagnosticIssue(
                "PYREVIT_VERSION_UNSUPPORTED",
                DiagnosticCategory.VersionCompatibility,
                DiagnosticSeverity.Error,
                "pyRevit Upgrade Required",
                $"Detected pyRevit {discovery.PyRevit.Version ?? "unknown"}, which does not support the MCP agent runtime.",
                "Upgrade to pyRevit 7.0 or newer (7.1+ recommended).",
                CanAutoRepair: false));
        }
        else if (!discovery.PyRevit.AgentHostEnabled)
        {
            issues.Add(new DiagnosticIssue(
                "PYREVIT_AGENT_DISABLED",
                DiagnosticCategory.PyRevitRuntime,
                DiagnosticSeverity.Warning,
                "pyRevit Agent Runtime Disabled",
                "pyRevit is installed, but its MCP agent host is currently disabled.",
                "Enable the agent host via AV Orch (runs 'pyrevit configs agent enable'), then reload pyRevit or restart Revit.",
                CanAutoRepair: true,
                RepairActionId: "ENABLE_PYREVIT_AGENT"));
        }

        if (sessions.Count == 0)
        {
            issues.Add(new DiagnosticIssue(
                "REVIT_NOT_RUNNING",
                DiagnosticCategory.RevitSession,
                DiagnosticSeverity.Warning,
                "No Active Revit Session",
                "No running Autodesk Revit session with an active pyRevit agent host responded.",
                "Launch Autodesk Revit, open a project, and verify pyRevit loaded without errors.",
                CanAutoRepair: false));
        }
        else
        {
            if (sessions.Any(s => s.IsElevated))
            {
                issues.Add(new DiagnosticIssue(
                    "REVIT_SESSION_ELEVATED",
                    DiagnosticCategory.RevitSession,
                    DiagnosticSeverity.Error,
                    "Revit Running Elevated",
                    "A Revit session is running with administrator privileges; pyRevit blocks the agent host in elevated sessions.",
                    "Close Revit and relaunch it as a normal user.",
                    CanAutoRepair: false));
            }

            if (sessions.All(s => string.IsNullOrWhiteSpace(s.ActiveDocumentTitle)))
            {
                issues.Add(new DiagnosticIssue(
                    "REVIT_NO_DOCUMENT",
                    DiagnosticCategory.RevitDocument,
                    DiagnosticSeverity.Info,
                    "No Active Revit Document",
                    "Revit is running, but no project or family document is currently open.",
                    "Open a Revit project to run read-only diagnostic queries and enable AI model interaction.",
                    CanAutoRepair: false));
            }
        }

        foreach (var provider in discovery.Providers)
        {
            if (provider.Detection.Status == InstallationStatus.InvalidPath)
            {
                issues.Add(new DiagnosticIssue(
                    $"PROVIDER_INVALID_PATH_{provider.Provider}",
                    DiagnosticCategory.ExecutablePath,
                    DiagnosticSeverity.Error,
                    $"{provider.DisplayName}: Invalid Executable Path",
                    provider.Detection.Message,
                    $"Reinstall {provider.DisplayName} or fix your system PATH.",
                    CanAutoRepair: false));
            }
            else if (provider.Detection.Status == InstallationStatus.UnsupportedVersion)
            {
                issues.Add(new DiagnosticIssue(
                    $"PROVIDER_UNSUPPORTED_VER_{provider.Provider}",
                    DiagnosticCategory.VersionCompatibility,
                    DiagnosticSeverity.Warning,
                    $"{provider.DisplayName}: Unsupported Version",
                    provider.Detection.Message,
                    $"Update {provider.DisplayName} to the latest official release.",
                    CanAutoRepair: false));
            }

            if (provider.Detection.Status == InstallationStatus.Installed &&
                provider.Authentication.Status is AuthenticationStatus.NotAuthenticated or AuthenticationStatus.Expired)
            {
                issues.Add(new DiagnosticIssue(
                    $"PROVIDER_AUTH_{provider.Provider}",
                    DiagnosticCategory.Authentication,
                    DiagnosticSeverity.Warning,
                    $"{provider.DisplayName}: Sign-In Required",
                    provider.Authentication.Message,
                    $"Sign in using {provider.DisplayName}'s official login flow.",
                    CanAutoRepair: false));
            }

            if (provider.Registration.Status == RegistrationStatus.CorruptConfig)
            {
                issues.Add(new DiagnosticIssue(
                    $"PROVIDER_CORRUPT_CONFIG_{provider.Provider}",
                    DiagnosticCategory.McpRegistration,
                    DiagnosticSeverity.Error,
                    $"{provider.DisplayName}: Corrupt Configuration File",
                    provider.Registration.Message,
                    $"Repair registration or restore a backup for {provider.DisplayName}.",
                    CanAutoRepair: true,
                    RepairActionId: $"REPAIR_PROVIDER_{provider.Provider}"));
            }
            else if (provider.Registration.Status == RegistrationStatus.Conflicting)
            {
                issues.Add(new DiagnosticIssue(
                    $"PROVIDER_CONFLICT_{provider.Provider}",
                    DiagnosticCategory.ConflictingRegistration,
                    DiagnosticSeverity.Warning,
                    $"{provider.DisplayName}: Conflicting MCP Registration",
                    provider.Registration.Message,
                    $"Repair registration to align with the detected pyRevit executable.",
                    CanAutoRepair: true,
                    RepairActionId: $"REPAIR_PROVIDER_{provider.Provider}"));
            }
        }

        var isHealthy = issues.All(i => i.Severity != DiagnosticSeverity.Error);
        return Task.FromResult(new DiagnosticReport(DateTimeOffset.UtcNow, issues, isHealthy));
    }
}
