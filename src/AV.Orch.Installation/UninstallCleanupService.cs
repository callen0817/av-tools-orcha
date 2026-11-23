using AV.Orch.Core.Abstractions;
using AV.Orch.Core.Models;

namespace AV.Orch.Installation;

public sealed record UninstallCleanupResult(
    bool Succeeded,
    IReadOnlyList<string> CleanedProviders,
    IReadOnlyList<string> SkippedProviders,
    IReadOnlyList<string> Errors);

public sealed class UninstallCleanupService
{
    private readonly IReadOnlyList<IAiProviderAdapter> _adapters;
    private readonly IAuditLogService _auditLog;
    private readonly IAppLogger _logger;
    private readonly AvToolsRibbonInstaller? _ribbonInstaller;

    public UninstallCleanupService(
        IEnumerable<IAiProviderAdapter> adapters,
        IAuditLogService auditLog,
        IAppLogger logger,
        AvToolsRibbonInstaller? ribbonInstaller = null)
    {
        _adapters = adapters.ToList();
        _auditLog = auditLog;
        _logger = logger;
        _ribbonInstaller = ribbonInstaller;
    }

    /// <summary>
    /// Removes ONLY AV Orch-owned MCP registrations across providers and ONLY the AV Orch button
    /// from the shared AV Tools Revit ribbon.
    /// Never uninstalls Revit, pyRevit, NX Xplode / other AV Tools, Codex, Gemini, Claude, user auth, or customer models.
    /// </summary>
    public async Task<UninstallCleanupResult> RemoveAllAvOrchRegistrationsAsync(CancellationToken cancellationToken = default)
    {
        var cleaned = new List<string>();
        var skipped = new List<string>();
        var errors = new List<string>();

        foreach (var adapter in _adapters)
        {
            var state = await adapter.GetRegistrationStateAsync(cancellationToken).ConfigureAwait(false);
            if (state.Status == RegistrationStatus.NotRegistered)
            {
                skipped.Add(adapter.DisplayName);
                continue;
            }

            if (!state.OwnedByAvOrch)
            {
                skipped.Add($"{adapter.DisplayName} (preserved non-AV-Orch entry)");
                continue;
            }

            var res = await adapter.RemoveMcpRegistrationAsync(cancellationToken).ConfigureAwait(false);
            if (res.Succeeded)
            {
                cleaned.Add(adapter.DisplayName);
                _auditLog.Record("UninstallCleanup", "Uninstaller", adapter.DisplayName, true, res.Message);
            }
            else
            {
                errors.Add($"{adapter.DisplayName}: {res.Message}");
                _logger.Error("UninstallCleanup", $"Failed to clean {adapter.DisplayName}: {res.Message}");
                _auditLog.Record("UninstallCleanup", "Uninstaller", adapter.DisplayName, false, res.Message);
            }
        }

        if (_ribbonInstaller is not null)
        {
            var ribbonRes = await _ribbonInstaller.RemoveAvOrchFromRibbonAsync(cancellationToken).ConfigureAwait(false);
            if (!ribbonRes.Succeeded)
            {
                errors.Add($"AV Tools Ribbon: {ribbonRes.Message}");
            }
        }

        return new UninstallCleanupResult(errors.Count == 0, cleaned, skipped, errors);
    }
}
