using System.Collections.ObjectModel;
using System.Windows;
using AV.Orch.Core.Abstractions;
using AV.Orch.Core.Models;
using AV.Orch.Installation;
using AV.Orch.Security;

namespace AV.Orch.Desktop.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly AvOrchOrchestrator _orchestrator;
    private readonly ISettingsService _settingsService;
    private readonly IAuditLogService _auditLog;
    private readonly IAppLogger _logger;
    private readonly UninstallCleanupService _uninstallCleanup;
    private readonly AvToolsRibbonInstaller _ribbonInstaller;

    private string _activeSection = "Home";
    private bool _isBusy;
    private string _statusBanner = "Ready. Click Refresh Status to inspect Revit, pyRevit, and AI providers.";
    private string _revitConnectionSummary = "Not checked";
    private string _activeDocumentSummary = "None";
    private string _pyRevitStateSummary = "Not checked";
    private string _avToolsRibbonSummary = "Not checked";
    private string _connectionHealthSummary = "Pending check";
    private string _lastDiagnosticSummary = "No diagnostic run yet.";
    private bool _isReadOnlyPolicy = true;
    private bool _isAskPolicy;
    private bool _darkTheme;
    private bool _launchAtStartup;
    private bool _checkForUpdates = true;

    public MainViewModel(
        AvOrchOrchestrator orchestrator,
        ISettingsService settingsService,
        IAuditLogService auditLog,
        IAppLogger logger,
        UninstallCleanupService uninstallCleanup,
        AvToolsRibbonInstaller ribbonInstaller)
    {
        _orchestrator = orchestrator;
        _settingsService = settingsService;
        _auditLog = auditLog;
        _logger = logger;
        _uninstallCleanup = uninstallCleanup;
        _ribbonInstaller = ribbonInstaller;

        LoadSettings();

        NavigateCommand = new RelayCommand(p => ActiveSection = p?.ToString() ?? "Home");
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        TestRevitCommand = new AsyncRelayCommand(TestRevitAsync);
        InstallAvToolsRibbonCommand = new AsyncRelayCommand(InstallAvToolsRibbonAsync);
        ConnectProviderCommand = new AsyncRelayCommand(p => ConnectProviderAsync(p));
        DisconnectProviderCommand = new AsyncRelayCommand(p => DisconnectProviderAsync(p));
        RepairProviderCommand = new AsyncRelayCommand(p => RepairProviderAsync(p));
        SignInProviderCommand = new AsyncRelayCommand(p => SignInProviderAsync(p));
        ApplyPermissionPolicyCommand = new AsyncRelayCommand(ApplyPermissionPolicyAsync);
        RunRepairActionCommand = new AsyncRelayCommand(p => RunRepairActionAsync(p?.ToString()));
        SaveSettingsCommand = new RelayCommand(SaveSettings);
        RestoreDefaultsCommand = new RelayCommand(RestoreDefaults);
        CleanAvOrchRegistrationsCommand = new AsyncRelayCommand(CleanAllRegistrationsAsync);
    }

    public string AppVersion => "v1.0.0";
    public string CompanyName => "Artificial Vision, Inc.";
    public string ModelDataNotice => TrustBoundaryDisclosure.ModelDataConfidentialityNotice;
    public string PythonExecutionNotice => TrustBoundaryDisclosure.PythonExecutionRiskNotice;
    public string NativeApprovalNotice => TrustBoundaryDisclosure.NativeApprovalNotice;
    public string LogDirectory => _logger.LogDirectory;
    public string SettingsPath => _settingsService.SettingsFilePath;

    public ObservableCollection<ProviderSnapshot> Providers { get; } = new();
    public ObservableCollection<RevitInstallation> RevitInstallations { get; } = new();
    public ObservableCollection<RevitSession> RevitSessions { get; } = new();
    public ObservableCollection<DiagnosticIssue> DiagnosticIssues { get; } = new();
    public ObservableCollection<AuditEntry> AuditEntries { get; } = new();

    public string ActiveSection
    {
        get => _activeSection;
        set => SetProperty(ref _activeSection, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public string StatusBanner
    {
        get => _statusBanner;
        set => SetProperty(ref _statusBanner, value);
    }

    public string RevitConnectionSummary
    {
        get => _revitConnectionSummary;
        set => SetProperty(ref _revitConnectionSummary, value);
    }

    public string ActiveDocumentSummary
    {
        get => _activeDocumentSummary;
        set => SetProperty(ref _activeDocumentSummary, value);
    }

    public string PyRevitStateSummary
    {
        get => _pyRevitStateSummary;
        set => SetProperty(ref _pyRevitStateSummary, value);
    }

    public string AvToolsRibbonSummary
    {
        get => _avToolsRibbonSummary;
        set => SetProperty(ref _avToolsRibbonSummary, value);
    }

    public string ConnectionHealthSummary
    {
        get => _connectionHealthSummary;
        set => SetProperty(ref _connectionHealthSummary, value);
    }

    public string LastDiagnosticSummary
    {
        get => _lastDiagnosticSummary;
        set => SetProperty(ref _lastDiagnosticSummary, value);
    }

    public bool IsReadOnlyPolicy
    {
        get => _isReadOnlyPolicy;
        set
        {
            if (SetProperty(ref _isReadOnlyPolicy, value) && value)
            {
                IsAskPolicy = false;
            }
        }
    }

    public bool IsAskPolicy
    {
        get => _isAskPolicy;
        set
        {
            if (SetProperty(ref _isAskPolicy, value) && value)
            {
                IsReadOnlyPolicy = false;
            }
        }
    }

    public bool DarkTheme
    {
        get => _darkTheme;
        set => SetProperty(ref _darkTheme, value);
    }

    public bool LaunchAtStartup
    {
        get => _launchAtStartup;
        set => SetProperty(ref _launchAtStartup, value);
    }

    public bool CheckForUpdates
    {
        get => _checkForUpdates;
        set => SetProperty(ref _checkForUpdates, value);
    }

    public RelayCommand NavigateCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand TestRevitCommand { get; }
    public AsyncRelayCommand InstallAvToolsRibbonCommand { get; }
    public AsyncRelayCommand ConnectProviderCommand { get; }
    public AsyncRelayCommand DisconnectProviderCommand { get; }
    public AsyncRelayCommand RepairProviderCommand { get; }
    public AsyncRelayCommand SignInProviderCommand { get; }
    public AsyncRelayCommand ApplyPermissionPolicyCommand { get; }
    public AsyncRelayCommand RunRepairActionCommand { get; }
    public RelayCommand SaveSettingsCommand { get; }
    public RelayCommand RestoreDefaultsCommand { get; }
    public AsyncRelayCommand CleanAvOrchRegistrationsCommand { get; }

    public event Action<bool>? ThemeChanged;

    public async Task RefreshAsync()
    {
        IsBusy = true;
        StatusBanner = "Scanning Revit, pyRevit, and AI providers...";
        try
        {
            var (discovery, sessions, diagnostics) = await _orchestrator.RefreshAllAsync();
            var ribbonStatus = _ribbonInstaller.InspectRibbon();
            AvToolsRibbonSummary = ribbonStatus.Summary;

            Providers.Clear();
            foreach (var p in discovery.Providers)
            {
                Providers.Add(p);
            }

            RevitInstallations.Clear();
            foreach (var r in discovery.RevitInstallations)
            {
                RevitInstallations.Add(r);
            }

            RevitSessions.Clear();
            foreach (var s in sessions)
            {
                RevitSessions.Add(s);
            }

            DiagnosticIssues.Clear();
            foreach (var issue in diagnostics.Issues)
            {
                DiagnosticIssues.Add(issue);
            }

            RefreshAuditEntries();

            PyRevitStateSummary = discovery.PyRevit.Message;
            RevitConnectionSummary = sessions.Count > 0
                ? $"{sessions.Count} active Revit session(s) responding"
                : "No active Revit session responding";
            ActiveDocumentSummary = sessions.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.ActiveDocumentTitle))?.ActiveDocumentTitle
                ?? "No active document";
            ConnectionHealthSummary = diagnostics.IsHealthy
                ? $"Healthy ({diagnostics.WarningCount} warning(s))"
                : $"Action Required ({diagnostics.ErrorCount} error(s), {diagnostics.WarningCount} warning(s))";
            LastDiagnosticSummary = $"Checked at {diagnostics.GeneratedAtUtc.ToLocalTime():HH:mm:ss} — {diagnostics.Issues.Count} finding(s).";
            StatusBanner = "Environment scan complete.";
        }
        catch (Exception ex)
        {
            _logger.Error("Desktop", "Refresh failed", ex);
            StatusBanner = $"Refresh failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task TestRevitAsync()
    {
        IsBusy = true;
        StatusBanner = "Running restricted read-only Revit context query...";
        try
        {
            var res = await _orchestrator.TestRevitReadOnlyConnectionAsync();
            LastDiagnosticSummary = res.Summary;
            StatusBanner = res.Succeeded ? res.Summary : $"Test failed: {res.Summary}";
            if (!string.IsNullOrWhiteSpace(res.ActiveDocumentTitle))
            {
                ActiveDocumentSummary = res.ActiveDocumentTitle;
            }

            RefreshAuditEntries();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task InstallAvToolsRibbonAsync()
    {
        IsBusy = true;
        StatusBanner = "Installing AV Orch onto shared AV Tools Revit ribbon...";
        try
        {
            var settings = _settingsService.Load();
            var res = await _ribbonInstaller.InstallOrUpdateRibbonAsync(settings.CustomPyRevitPath);
            StatusBanner = res.Message;
            AvToolsRibbonSummary = _ribbonInstaller.InspectRibbon().Summary;
            RefreshAuditEntries();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ConnectProviderAsync(object? parameter)
    {
        if (parameter is not ProviderId providerId)
        {
            return;
        }

        var confirm = MessageBox.Show(
            $"AV Orch will back up your existing {providerId} configuration and add the local pyRevit MCP server entry.\n\n" +
            $"{TrustBoundaryDisclosure.ModelDataConfidentialityNotice}\n\nProceed?",
            "Confirm Provider Registration",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        var res = await _orchestrator.ConnectProviderAsync(providerId, userApprovedConfigChange: true);
        StatusBanner = res.Message;
        await RefreshAsync();
    }

    private async Task DisconnectProviderAsync(object? parameter)
    {
        if (parameter is not ProviderId providerId)
        {
            return;
        }

        var res = await _orchestrator.DisconnectProviderAsync(providerId);
        StatusBanner = res.Message;
        await RefreshAsync();
    }

    private async Task RepairProviderAsync(object? parameter)
    {
        if (parameter is not ProviderId providerId)
        {
            return;
        }

        var res = await _orchestrator.RepairProviderAsync(providerId);
        StatusBanner = res.Message;
        await RefreshAsync();
    }

    private async Task SignInProviderAsync(object? parameter)
    {
        if (parameter is not ProviderId providerId)
        {
            return;
        }

        var res = await _orchestrator.BeginProviderLoginAsync(providerId);
        StatusBanner = res.Message;
        RefreshAuditEntries();
    }

    private async Task ApplyPermissionPolicyAsync()
    {
        var target = IsAskPolicy ? PermissionPolicy.AskBeforeModifications : PermissionPolicy.ReadOnly;
        var approved = true;

        if (target == PermissionPolicy.AskBeforeModifications)
        {
            var answer = MessageBox.Show(
                "Enable 'Ask Before Modifications'?\n\n" +
                "AI assistants will be allowed to propose model edits. Every modification will require your explicit Keep/Discard approval inside Autodesk Revit.\n\n" +
                TrustBoundaryDisclosure.PythonExecutionRiskNotice,
                "Approve Permission Policy Change",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            approved = answer == MessageBoxResult.Yes;
        }

        var res = await _orchestrator.SetPermissionPolicyAsync(target, approved);
        StatusBanner = res.Message;
        if (!res.Succeeded)
        {
            LoadSettings();
        }

        RefreshAuditEntries();
    }

    private async Task RunRepairActionAsync(string? repairActionId)
    {
        if (string.IsNullOrWhiteSpace(repairActionId))
        {
            return;
        }

        var res = await _orchestrator.ExecuteDiagnosticRepairAsync(repairActionId);
        StatusBanner = res.Message;
        await RefreshAsync();
    }

    private async Task CleanAllRegistrationsAsync()
    {
        var confirm = MessageBox.Show(
            "Remove all AV Orch-owned pyRevit MCP registrations across Codex, Gemini, and Claude Code?\n\n" +
            "Your other MCP servers, provider logins, Revit, pyRevit, and project files will NOT be touched.",
            "Clean AV Orch Registrations",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        var res = await _uninstallCleanup.RemoveAllAvOrchRegistrationsAsync();
        StatusBanner = res.Succeeded
            ? $"Cleaned {res.CleanedProviders.Count} provider registration(s)."
            : $"Cleanup finished with {res.Errors.Count} error(s).";
        await RefreshAsync();
    }

    private void LoadSettings()
    {
        var s = _settingsService.Load();
        IsReadOnlyPolicy = s.PermissionPolicy == PermissionPolicy.ReadOnly;
        IsAskPolicy = s.PermissionPolicy == PermissionPolicy.AskBeforeModifications;
        DarkTheme = s.DarkTheme;
        LaunchAtStartup = s.LaunchAtStartup;
        CheckForUpdates = s.CheckForUpdates;
    }

    private void SaveSettings()
    {
        var current = _settingsService.Load();
        var updated = current with
        {
            DarkTheme = DarkTheme,
            LaunchAtStartup = LaunchAtStartup,
            CheckForUpdates = CheckForUpdates,
            TelemetryEnabled = false
        };
        _settingsService.Save(updated);
        ThemeChanged?.Invoke(DarkTheme);
        StatusBanner = "Settings saved locally.";
    }

    private void RestoreDefaults()
    {
        _settingsService.RestoreDefaults();
        LoadSettings();
        ThemeChanged?.Invoke(DarkTheme);
        StatusBanner = "Settings restored to defaults (Read-Only, telemetry off).";
    }

    private void RefreshAuditEntries()
    {
        AuditEntries.Clear();
        foreach (var entry in _auditLog.GetRecentEntries(30))
        {
            AuditEntries.Add(entry);
        }
    }
}
