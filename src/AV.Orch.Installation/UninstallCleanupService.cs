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

    public UninstallCleanupService(
        IEnumerable<IAiProviderAdapter> adapters,
        IAuditLogService auditLog,
        IAppLogger logger)
    {
        _adapters = adapters.ToList();
        _auditLog = auditLog;
        _logger = logger;
    }

    /// <summary>
    /// Removes ONLY AV Orch-owned MCP registrations across providers.
    /// Never uninstalls Revit, pyRevit, Codex, Gemini, Claude, user auth, or customer models.
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

        return new UninstallCleanupResult(errors.Count == 0, cleaned, skipped, errors);
    }
}
