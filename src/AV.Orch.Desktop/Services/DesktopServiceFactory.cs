using AV.Orch.Core.Abstractions;
using AV.Orch.Core.Services;
using AV.Orch.Desktop.ViewModels;
using AV.Orch.Installation;
using AV.Orch.Providers.Claude;
using AV.Orch.Providers.Codex;
using AV.Orch.Providers.Gemini;
using AV.Orch.Revit;
using AV.Orch.Security;

namespace AV.Orch.Desktop.Services;

public sealed record DesktopComposition(
    MainViewModel MainViewModel,
    UninstallCleanupService UninstallCleanupService,
    AvToolsRibbonInstaller RibbonInstaller,
    ISettingsService SettingsService,
    IAppLogger Logger);

public static class DesktopServiceFactory
{
    public static DesktopComposition Create()
    {
        IFileSystem fileSystem = new LocalFileSystem();
        IEnvironmentProvider environment = new SystemEnvironmentProvider();
        ISecretRedactor redactor = new SecretRedactor();
        IProcessRunner processRunner = new SystemProcessRunner();
        ISettingsService settingsService = new JsonSettingsService(fileSystem, environment);
        IAppLogger logger = new StructuredAppLogger(fileSystem, environment, redactor);
        IAuditLogService auditLog = new AuditLogService(fileSystem, environment, redactor);
        IConfigurationBackupService backupService = new ConfigurationBackupService(fileSystem, environment);
        IMcpStdioProbe mcpProbe = new McpStdioProbe();

        IRevitDiscoveryService revitDiscovery = new RevitDiscoveryService(fileSystem, environment);
        IPyRevitService pyRevitService = new PyRevitService(processRunner, fileSystem, environment, logger);
        IDiagnosticsService diagnosticsService = new DiagnosticsService(environment);

        IAiProviderAdapter[] adapters =
        {
            new CodexAdapter(processRunner, fileSystem, environment, backupService, mcpProbe, logger),
            new GeminiAdapter(processRunner, fileSystem, environment, backupService, mcpProbe, logger),
            new ClaudeCodeAdapter(processRunner, fileSystem, environment, backupService, mcpProbe, logger)
        };

        IDependencyDiscoveryService dependencyDiscovery = new DependencyDiscoveryService(revitDiscovery, pyRevitService, adapters);
        var orchestrator = new AvOrchOrchestrator(
            dependencyDiscovery,
            pyRevitService,
            diagnosticsService,
            settingsService,
            auditLog,
            logger,
            adapters);

        var ribbonInstaller = new AvToolsRibbonInstaller(fileSystem, environment, processRunner, logger, auditLog);
        var uninstallCleanup = new UninstallCleanupService(adapters, auditLog, logger, ribbonInstaller);
        var installWorkflow = new DependencyInstallWorkflow(processRunner, auditLog);
        var vm = new MainViewModel(orchestrator, settingsService, auditLog, logger, uninstallCleanup, ribbonInstaller, installWorkflow);
        return new DesktopComposition(vm, uninstallCleanup, ribbonInstaller, settingsService, logger);
    }
}
