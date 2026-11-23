using AV.Orch.Core.Abstractions;
using AV.Orch.Core.Models;
using AV.Orch.Security;

namespace AV.Orch.Installation;

public sealed class AvOrchOrchestrator
{
    private readonly IDependencyDiscoveryService _discoveryService;
    private readonly IPyRevitService _pyRevitService;
    private readonly IDiagnosticsService _diagnosticsService;
    private readonly ISettingsService _settingsService;
    private readonly IAuditLogService _auditLog;
    private readonly IAppLogger _logger;
    private readonly Dictionary<ProviderId, IAiProviderAdapter> _adapters;

    public AvOrchOrchestrator(
        IDependencyDiscoveryService discoveryService,
        IPyRevitService pyRevitService,
        IDiagnosticsService diagnosticsService,
        ISettingsService settingsService,
        IAuditLogService auditLog,
        IAppLogger logger,
        IEnumerable<IAiProviderAdapter> adapters)
    {
        _discoveryService = discoveryService;
        _pyRevitService = pyRevitService;
        _diagnosticsService = diagnosticsService;
        _settingsService = settingsService;
        _auditLog = auditLog;
        _logger = logger;
        _adapters = adapters.ToDictionary(a => a.ProviderId);
    }

    public async Task<(DependencyDiscoverySummary Discovery, IReadOnlyList<RevitSession> Sessions, DiagnosticReport Diagnostics)> RefreshAllAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = _settingsService.Load();
        var discovery = await _discoveryService.DiscoverAllAsync(settings.CustomPyRevitPath, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<RevitSession> sessions = Array.Empty<RevitSession>();
        if (discovery.PyRevit.IsInstalled && !string.IsNullOrWhiteSpace(discovery.PyRevit.ExecutablePath))
        {
            sessions = await _pyRevitService.GetRunningSessionsAsync(discovery.PyRevit.ExecutablePath, cancellationToken).ConfigureAwait(false);
        }

        var diagnostics = await _diagnosticsService.RunDiagnosticsAsync(discovery, sessions, cancellationToken).ConfigureAwait(false);
        return (discovery, sessions, diagnostics);
    }

    public async Task<OperationResult> ConnectProviderAsync(
        ProviderId providerId,
        bool userApprovedConfigChange,
        CancellationToken cancellationToken = default)
    {
        if (!userApprovedConfigChange)
        {
            return OperationResult.Fail("Explicit user approval is required before modifying provider configuration.", "APPROVAL_REQUIRED");
        }

        if (!_adapters.TryGetValue(providerId, out var adapter))
        {
            return OperationResult.Fail($"Unknown provider '{providerId}'.", "UNKNOWN_PROVIDER");
        }

        var settings = _settingsService.Load();
        var pyRevit = await _pyRevitService.DetectPyRevitAsync(settings.CustomPyRevitPath, cancellationToken).ConfigureAwait(false);
        if (!pyRevit.IsInstalled || !pyRevit.SupportsAgentRuntime || string.IsNullOrWhiteSpace(pyRevit.ExecutablePath))
        {
            return OperationResult.Fail(
                "pyRevit 7.0+ with MCP agent runtime must be installed before connecting a provider.",
                "PYREVIT_REQUIRED",
                "Install pyRevit 7.1+ from official releases.");
        }

        if (!pyRevit.AgentHostEnabled)
        {
            var enableRes = await _pyRevitService.EnableAgentHostAsync(pyRevit.ExecutablePath, cancellationToken).ConfigureAwait(false);
            if (!enableRes.Succeeded)
            {
                return enableRes;
            }
        }

        // Enforce saved permission policy (defaults to ReadOnly)
        await _pyRevitService.SetAgentPolicyAsync(pyRevit.ExecutablePath, settings.PermissionPolicy, cancellationToken).ConfigureAwait(false);

        var revitYear = settings.PreferredRevitYear > 0 ? settings.PreferredRevitYear : (int?)null;
        var installRes = await adapter.InstallMcpRegistrationAsync(pyRevit.ExecutablePath, revitYear, cancellationToken).ConfigureAwait(false);

        _auditLog.Record("ConnectProvider", "User", adapter.DisplayName, installRes.Succeeded, installRes.Message);
        _logger.Info("Orchestrator", $"ConnectProvider({providerId}): {installRes.Succeeded} - {installRes.Message}");
        return installRes;
    }

    public async Task<OperationResult> DisconnectProviderAsync(ProviderId providerId, CancellationToken cancellationToken = default)
    {
        if (!_adapters.TryGetValue(providerId, out var adapter))
        {
            return OperationResult.Fail($"Unknown provider '{providerId}'.", "UNKNOWN_PROVIDER");
        }

        var res = await adapter.RemoveMcpRegistrationAsync(cancellationToken).ConfigureAwait(false);
        _auditLog.Record("DisconnectProvider", "User", adapter.DisplayName, res.Succeeded, res.Message);
        return res;
    }

    public async Task<OperationResult> RepairProviderAsync(ProviderId providerId, CancellationToken cancellationToken = default)
    {
        if (!_adapters.TryGetValue(providerId, out var adapter))
        {
            return OperationResult.Fail($"Unknown provider '{providerId}'.", "UNKNOWN_PROVIDER");
        }

        var settings = _settingsService.Load();
        var pyRevit = await _pyRevitService.DetectPyRevitAsync(settings.CustomPyRevitPath, cancellationToken).ConfigureAwait(false);
        if (!pyRevit.IsInstalled || string.IsNullOrWhiteSpace(pyRevit.ExecutablePath))
        {
            return OperationResult.Fail("pyRevit executable is required to repair registration.", "PYREVIT_REQUIRED");
        }

        var revitYear = settings.PreferredRevitYear > 0 ? settings.PreferredRevitYear : (int?)null;
        var res = await adapter.RepairRegistrationAsync(pyRevit.ExecutablePath, revitYear, cancellationToken).ConfigureAwait(false);
        _auditLog.Record("RepairProvider", "User", adapter.DisplayName, res.Succeeded, res.Message);
        return res;
    }

    public async Task<OperationResult> BeginProviderLoginAsync(ProviderId providerId, CancellationToken cancellationToken = default)
    {
        if (!_adapters.TryGetValue(providerId, out var adapter))
        {
            return OperationResult.Fail($"Unknown provider '{providerId}'.", "UNKNOWN_PROVIDER");
        }

        var res = await adapter.BeginProviderLoginAsync(cancellationToken).ConfigureAwait(false);
        _auditLog.Record("BeginProviderLogin", "User", adapter.DisplayName, res.Succeeded, res.Message);
        return res;
    }

    public async Task<OperationResult> SetPermissionPolicyAsync(
        PermissionPolicy targetPolicy,
        bool userExplicitlyApproved,
        CancellationToken cancellationToken = default)
    {
        var currentSettings = _settingsService.Load();
        var guardRes = PermissionPolicyGuard.ValidatePolicyTransition(
            currentSettings.PermissionPolicy,
            targetPolicy,
            userExplicitlyApproved);

        if (!guardRes.Succeeded)
        {
            _auditLog.Record("ChangePermissionPolicy", "User", targetPolicy.ToString(), false, guardRes.Message);
            return guardRes;
        }

        var pyRevit = await _pyRevitService.DetectPyRevitAsync(currentSettings.CustomPyRevitPath, cancellationToken).ConfigureAwait(false);
        if (pyRevit.IsInstalled && pyRevit.SupportsAgentRuntime && !string.IsNullOrWhiteSpace(pyRevit.ExecutablePath))
        {
            var pyRes = await _pyRevitService.SetAgentPolicyAsync(pyRevit.ExecutablePath, targetPolicy, cancellationToken).ConfigureAwait(false);
            if (!pyRes.Succeeded)
            {
                return pyRes;
            }
        }

        _settingsService.Save(currentSettings with { PermissionPolicy = targetPolicy });
        _auditLog.Record("ChangePermissionPolicy", "User", targetPolicy.ToString(), true, $"Permission policy changed to {targetPolicy}.");
        return OperationResult.Ok($"Permission policy set to {targetPolicy}.");
    }

    public async Task<RevitDiagnosticResult> TestRevitReadOnlyConnectionAsync(CancellationToken cancellationToken = default)
    {
        var settings = _settingsService.Load();
        var pyRevit = await _pyRevitService.DetectPyRevitAsync(settings.CustomPyRevitPath, cancellationToken).ConfigureAwait(false);
        if (!pyRevit.IsInstalled || string.IsNullOrWhiteSpace(pyRevit.ExecutablePath))
        {
            return new RevitDiagnosticResult(false, "pyRevit is not installed.", null, null, "PYREVIT_NOT_FOUND");
        }

        var year = settings.PreferredRevitYear > 0 ? settings.PreferredRevitYear : (int?)null;
        var res = await _pyRevitService.RunReadOnlyDiagnosticQueryAsync(pyRevit.ExecutablePath, year, cancellationToken).ConfigureAwait(false);
        _auditLog.Record("RevitReadOnlyTest", "User", res.ActiveDocumentTitle ?? "Revit", res.Succeeded, res.Summary);
        return res;
    }

    public async Task<OperationResult> ExecuteDiagnosticRepairAsync(string repairActionId, CancellationToken cancellationToken = default)
    {
        var settings = _settingsService.Load();
        if (string.Equals(repairActionId, "ENABLE_PYREVIT_AGENT", StringComparison.OrdinalIgnoreCase))
        {
            var pyRevit = await _pyRevitService.DetectPyRevitAsync(settings.CustomPyRevitPath, cancellationToken).ConfigureAwait(false);
            if (!pyRevit.IsInstalled || string.IsNullOrWhiteSpace(pyRevit.ExecutablePath))
            {
                return OperationResult.Fail("pyRevit executable not found.", "PYREVIT_NOT_FOUND");
            }

            var res = await _pyRevitService.EnableAgentHostAsync(pyRevit.ExecutablePath, cancellationToken).ConfigureAwait(false);
            _auditLog.Record("DiagnosticRepair", "User", repairActionId, res.Succeeded, res.Message);
            return res;
        }

        if (repairActionId.StartsWith("REPAIR_PROVIDER_", StringComparison.OrdinalIgnoreCase))
        {
            var providerName = repairActionId["REPAIR_PROVIDER_".Length..];
            if (Enum.TryParse<ProviderId>(providerName, ignoreCase: true, out var providerId))
            {
                return await RepairProviderAsync(providerId, cancellationToken).ConfigureAwait(false);
            }
        }

        return OperationResult.Fail($"Unknown repair action '{repairActionId}'.", "UNKNOWN_REPAIR_ACTION");
    }
}
