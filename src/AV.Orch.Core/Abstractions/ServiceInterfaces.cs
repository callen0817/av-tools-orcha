using AV.Orch.Core.Models;

namespace AV.Orch.Core.Abstractions;

public interface IRevitDiscoveryService
{
    Task<IReadOnlyList<RevitInstallation>> DiscoverInstallationsAsync(CancellationToken cancellationToken = default);
}

public interface IPyRevitService
{
    Task<PyRevitInfo> DetectPyRevitAsync(string? customPath = null, CancellationToken cancellationToken = default);
    Task<OperationResult> EnableAgentHostAsync(string pyRevitExePath, CancellationToken cancellationToken = default);
    Task<OperationResult> DisableAgentHostAsync(string pyRevitExePath, CancellationToken cancellationToken = default);
    Task<OperationResult> SetAgentPolicyAsync(string pyRevitExePath, PermissionPolicy policy, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RevitSession>> GetRunningSessionsAsync(string pyRevitExePath, CancellationToken cancellationToken = default);
    Task<RevitDiagnosticResult> RunReadOnlyDiagnosticQueryAsync(string pyRevitExePath, int? revitYear = null, CancellationToken cancellationToken = default);
}

public interface IMcpStdioProbe
{
    Task<OperationResult> ProbeInitializeAndToolsAsync(
        string command,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}

public interface IDependencyDiscoveryService
{
    Task<DependencyDiscoverySummary> DiscoverAllAsync(string? customPyRevitPath = null, CancellationToken cancellationToken = default);
}

public sealed record DependencyDiscoverySummary(
    IReadOnlyList<RevitInstallation> RevitInstallations,
    PyRevitInfo PyRevit,
    IReadOnlyList<ProviderSnapshot> Providers,
    DateTimeOffset DiscoveredAtUtc);

public interface IDiagnosticsService
{
    Task<DiagnosticReport> RunDiagnosticsAsync(
        DependencyDiscoverySummary discovery,
        IReadOnlyList<RevitSession> sessions,
        CancellationToken cancellationToken = default);
}
