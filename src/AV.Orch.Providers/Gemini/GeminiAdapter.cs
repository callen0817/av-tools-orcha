using System.Text.RegularExpressions;
using AV.Orch.Core.Abstractions;
using AV.Orch.Core.Models;
using AV.Orch.Providers.Common;
using AV.Orch.Security;

namespace AV.Orch.Providers.Gemini;

public sealed class GeminiAdapter : IAiProviderAdapter
{
    private static readonly Regex SemVerRegex = new(@"(?<ver>\d+\.\d+(?:\.\d+)?)", RegexOptions.Compiled);

    private readonly IProcessRunner _processRunner;
    private readonly IFileSystem _fileSystem;
    private readonly IEnvironmentProvider _environment;
    private readonly IConfigurationBackupService _backupService;
    private readonly IMcpStdioProbe _mcpProbe;
    private readonly IAppLogger _logger;

    public ProviderId ProviderId => ProviderId.Gemini;
    public string DisplayName => "Google Gemini CLI";
    public string VendorName => "Google";
    public string ServerEntryName => "pyrevit";

    public GeminiAdapter(
        IProcessRunner processRunner,
        IFileSystem fileSystem,
        IEnvironmentProvider environment,
        IConfigurationBackupService backupService,
        IMcpStdioProbe mcpProbe,
        IAppLogger logger)
    {
        _processRunner = processRunner;
        _fileSystem = fileSystem;
        _environment = environment;
        _backupService = backupService;
        _mcpProbe = mcpProbe;
        _logger = logger;
    }

    public string GetUserConfigPath() =>
        Path.Combine(_environment.UserProfileDirectory, ".gemini", "settings.json");

    public async Task<ProviderDetectionResult> DetectInstallationAsync(CancellationToken cancellationToken = default)
    {
        var exePath = await _processRunner.FindExecutableInPathAsync("gemini", cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return new ProviderDetectionResult(
                ProviderId,
                InstallationStatus.NotInstalled,
                null,
                null,
                "Google Gemini CLI ('gemini') was not found on PATH.",
                IsSupportedVersion: false);
        }

        var pathCheck = PathValidator.ValidateLocalExecutablePath(exePath);
        if (!pathCheck.Succeeded)
        {
            return new ProviderDetectionResult(
                ProviderId,
                InstallationStatus.InvalidPath,
                exePath,
                null,
                pathCheck.Message,
                IsSupportedVersion: false);
        }

        var version = await GetInstalledVersionAsync(cancellationToken).ConfigureAwait(false);
        return new ProviderDetectionResult(
            ProviderId,
            InstallationStatus.Installed,
            exePath,
            version,
            $"Google Gemini CLI {(version ?? "(version unknown)")} detected at '{exePath}'.",
            IsSupportedVersion: true);
    }

    public async Task<string?> GetInstalledVersionAsync(CancellationToken cancellationToken = default)
    {
        var res = await _processRunner.RunAsync("gemini", new[] { "--version" }, timeout: TimeSpan.FromSeconds(10), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!res.Succeeded)
        {
            return null;
        }

        var match = SemVerRegex.Match(res.StandardOutput);
        return match.Success ? match.Groups["ver"].Value : res.StandardOutput.Trim();
    }

    public async Task<ProviderAuthResult> CheckAuthenticationAsync(CancellationToken cancellationToken = default)
    {
        var detection = await DetectInstallationAsync(cancellationToken).ConfigureAwait(false);
        if (detection.Status != InstallationStatus.Installed)
        {
            return new ProviderAuthResult(
                ProviderId,
                AuthenticationStatus.CliUnavailable,
                "Install Google Gemini CLI before checking authentication.",
                "gemini");
        }

        // Check presence of Google OAuth credentials file or API key env var without ever reading/logging secret contents
        var oauthCredsPath = Path.Combine(_environment.UserProfileDirectory, ".gemini", "oauth_creds.json");
        var googleAccountsPath = Path.Combine(_environment.UserProfileDirectory, ".gemini", "google_accounts.json");

        if (_fileSystem.FileExists(oauthCredsPath) || _fileSystem.FileExists(googleAccountsPath))
        {
            return new ProviderAuthResult(
                ProviderId,
                AuthenticationStatus.Authenticated,
                "Google account credentials detected for Gemini CLI.",
                "gemini");
        }

        if (!string.IsNullOrWhiteSpace(_environment.GetEnvironmentVariable("GEMINI_API_KEY")) ||
            !string.IsNullOrWhiteSpace(_environment.GetEnvironmentVariable("GOOGLE_API_KEY")))
        {
            return new ProviderAuthResult(
                ProviderId,
                AuthenticationStatus.Authenticated,
                "Authenticated via GEMINI_API_KEY / GOOGLE_API_KEY environment variable.",
                "gemini");
        }

        return new ProviderAuthResult(
            ProviderId,
            AuthenticationStatus.NotAuthenticated,
            "Not signed in to Gemini CLI. Click Sign In to launch Gemini CLI's official authentication prompt.",
            "gemini");
    }

    public async Task<OperationResult> BeginProviderLoginAsync(CancellationToken cancellationToken = default)
    {
        var detection = await DetectInstallationAsync(cancellationToken).ConfigureAwait(false);
        if (detection.Status != InstallationStatus.Installed || string.IsNullOrWhiteSpace(detection.ExecutablePath))
        {
            return OperationResult.Fail("Google Gemini CLI is not installed.", "CLI_NOT_FOUND", "Install Gemini CLI from https://geminicli.com/docs/.");
        }

        return await _processRunner.LaunchInteractiveAsync(detection.ExecutablePath, Array.Empty<string>(), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<ProviderRegistrationState> GetRegistrationStateAsync(CancellationToken cancellationToken = default)
    {
        var configPath = GetUserConfigPath();
        return Task.FromResult(JsonMcpConfigHelper.InspectServerEntry(ProviderId, _fileSystem, configPath, ServerEntryName));
    }

    public Task<OperationResult> InstallMcpRegistrationAsync(string pyRevitExecutablePath, int? revitYear = null, CancellationToken cancellationToken = default)
    {
        var pathCheck = PathValidator.ValidateLocalExecutablePath(pyRevitExecutablePath);
        if (!pathCheck.Succeeded)
        {
            return Task.FromResult(pathCheck);
        }

        // pyRevit does not provide a native 'pyrevit mcp install gemini' command in 7.x,
        // so AV Orch performs a surgical JSON registration into ~/.gemini/settings.json with trust: false.
        var configPath = GetUserConfigPath();
        var args = revitYear.HasValue
            ? new[] { "mcp", $"--revit={revitYear.Value}" }
            : new[] { "mcp" };

        var result = JsonMcpConfigHelper.UpsertServerEntry(
            _fileSystem,
            _backupService,
            "gemini",
            configPath,
            ServerEntryName,
            pyRevitExecutablePath,
            args,
            includeStdioType: false,
            includeTrustFalse: true);

        if (result.Succeeded)
        {
            _logger.Info("GeminiAdapter", $"Registered pyRevit stdio MCP server in Gemini CLI settings '{configPath}'.");
        }

        return Task.FromResult(result);
    }

    public Task<OperationResult> RemoveMcpRegistrationAsync(CancellationToken cancellationToken = default)
    {
        var configPath = GetUserConfigPath();
        var result = JsonMcpConfigHelper.RemoveOwnedServerEntry(_fileSystem, _backupService, "gemini", configPath, ServerEntryName);
        if (result.Succeeded)
        {
            _logger.Info("GeminiAdapter", "Removed AV Orch pyRevit MCP registration from Gemini CLI.");
        }

        return Task.FromResult(result);
    }

    public async Task<OperationResult> ValidateConnectionAsync(string pyRevitExecutablePath, CancellationToken cancellationToken = default)
    {
        var detection = await DetectInstallationAsync(cancellationToken).ConfigureAwait(false);
        if (detection.Status != InstallationStatus.Installed)
        {
            return OperationResult.Fail("Google Gemini CLI is not installed.", "CLI_NOT_FOUND");
        }

        var reg = await GetRegistrationStateAsync(cancellationToken).ConfigureAwait(false);
        if (reg.Status != RegistrationStatus.Registered || string.IsNullOrWhiteSpace(reg.RegisteredCommand))
        {
            return OperationResult.Fail($"Gemini CLI MCP registration is not active ({reg.Status}).", "NOT_REGISTERED");
        }

        var probe = await _mcpProbe.ProbeInitializeAndToolsAsync(reg.RegisteredCommand, reg.RegisteredArgs, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!probe.Succeeded)
        {
            return probe;
        }

        return OperationResult.Ok("Google Gemini CLI MCP registration and pyRevit stdio server validated.");
    }

    public async Task<OperationResult> RepairRegistrationAsync(string pyRevitExecutablePath, int? revitYear = null, CancellationToken cancellationToken = default)
    {
        var configPath = GetUserConfigPath();
        var state = await GetRegistrationStateAsync(cancellationToken).ConfigureAwait(false);
        if (state.Status == RegistrationStatus.CorruptConfig)
        {
            var backup = _backupService.CreateBackup(configPath, "gemini");
            _fileSystem.WriteAllText(configPath, "{}" + Environment.NewLine);
            _logger.Warn("GeminiAdapter", $"Backed up corrupt Gemini settings.json to '{backup.BackupPath}' and initialized clean JSON.");
        }

        return await InstallMcpRegistrationAsync(pyRevitExecutablePath, revitYear, cancellationToken).ConfigureAwait(false);
    }
}
