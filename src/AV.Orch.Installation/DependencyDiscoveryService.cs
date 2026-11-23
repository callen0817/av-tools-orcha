using AV.Orch.Core.Abstractions;
using AV.Orch.Core.Models;

namespace AV.Orch.Installation;

public sealed class DependencyDiscoveryService : IDependencyDiscoveryService
{
    private readonly IRevitDiscoveryService _revitDiscovery;
    private readonly IPyRevitService _pyRevitService;
    private readonly IReadOnlyList<IAiProviderAdapter> _adapters;

    public DependencyDiscoveryService(
        IRevitDiscoveryService revitDiscovery,
        IPyRevitService pyRevitService,
        IEnumerable<IAiProviderAdapter> adapters)
    {
        _revitDiscovery = revitDiscovery;
        _pyRevitService = pyRevitService;
        _adapters = adapters.ToList();
    }

    public async Task<DependencyDiscoverySummary> DiscoverAllAsync(string? customPyRevitPath = null, CancellationToken cancellationToken = default)
    {
        var revitTask = _revitDiscovery.DiscoverInstallationsAsync(cancellationToken);
        var pyRevitTask = _pyRevitService.DetectPyRevitAsync(customPyRevitPath, cancellationToken);

        await Task.WhenAll(revitTask, pyRevitTask).ConfigureAwait(false);

        var snapshots = new List<ProviderSnapshot>();
        foreach (var adapter in _adapters)
        {
            var detection = await adapter.DetectInstallationAsync(cancellationToken).ConfigureAwait(false);
            var auth = await adapter.CheckAuthenticationAsync(cancellationToken).ConfigureAwait(false);
            var registration = await adapter.GetRegistrationStateAsync(cancellationToken).ConfigureAwait(false);

            snapshots.Add(new ProviderSnapshot(
                adapter.ProviderId,
                adapter.DisplayName,
                adapter.VendorName,
                detection,
                auth,
                registration,
                DateTimeOffset.UtcNow));
        }

        return new DependencyDiscoverySummary(
            revitTask.Result,
            pyRevitTask.Result,
            snapshots,
            DateTimeOffset.UtcNow);
    }
}
