using System.Text.RegularExpressions;
using AV.Orch.Core.Abstractions;
using AV.Orch.Core.Models;
using AV.Orch.Providers.Common;
using AV.Orch.Security;

namespace AV.Orch.Providers.Codex;

public sealed class CodexAdapter : IAiProviderAdapter
{
    private static readonly Regex SemVerRegex = new(@"(?<ver>\d+\.\d+(?:\.\d+)?)", RegexOptions.Compiled);

    private readonly IProcessRunner _processRunner;
    private readonly IFileSystem _fileSystem;
    private readonly IEnvironmentProvider _environment;
    private readonly IConfigurationBackupService _backupService;
    private readonly IMcpStdioProbe _mcpProbe;
    private readonly IAppLogger _logger;

    public ProviderId ProviderId => ProviderId.Codex;
    public string DisplayName => "OpenAI Codex";
    public string VendorName => "OpenAI";
    public string ServerEntryName => "pyrevit";

    public CodexAdapter(
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

    public string GetUserConfigPath()
    {
        var codexHome = _environment.GetEnvironmentVariable("CODEX_HOME");
        return !string.IsNullOrWhiteSpace(codexHome)
            ? Path.Combine(codexHome, "config.toml")
            : Path.Combine(_environment.UserProfileDirectory, ".codex", "config.toml");
    }

    public async Task<ProviderDetectionResult> DetectInstallationAsync(CancellationToken cancellationToken = default)
    {
        var exePath = await _processRunner.FindExecutableInPathAsync("codex", cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return new ProviderDetectionResult(
                ProviderId,
                InstallationStatus.NotInstalled,
                null,
                null,
                "OpenAI Codex CLI ('codex') was not found on PATH.",
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
            $"OpenAI Codex CLI {(version ?? "(version unknown)")} detected at '{exePath}'.",
            IsSupportedVersion: true);
    }

    public async Task<string?> GetInstalledVersionAsync(CancellationToken cancellationToken = default)
    {
        var res = await _processRunner.RunAsync("codex", new[] { "--version" }, timeout: TimeSpan.FromSeconds(10), cancellationToken: cancellationToken)
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
        if (detection.Status != InstallationStatus.Installed || string.IsNullOrWhiteSpace(detection.ExecutablePath))
        {
            return new ProviderAuthResult(
                ProviderId,
                AuthenticationStatus.CliUnavailable,
                "Install OpenAI Codex CLI before checking authentication.",
                "codex login");
        }

        var res = await _processRunner.RunAsync(detection.ExecutablePath, new[] { "login", "status" }, timeout: TimeSpan.FromSeconds(10), cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var combined = $"{res.StandardOutput}\n{res.StandardError}".ToLowerInvariant();
        if (combined.Contains("expired"))
        {
            return new ProviderAuthResult(ProviderId, AuthenticationStatus.Expired, "OpenAI Codex session expired. Please sign in again.", "codex login");
        }

        if (res.Succeeded && (combined.Contains("logged in") || combined.Contains("authenticated") || combined.Contains("chatgpt") || combined.Contains("api key")))
        {
            return new ProviderAuthResult(ProviderId, AuthenticationStatus.Authenticated, "Authenticated with OpenAI Codex.", "codex login");
        }

        if (!string.IsNullOrWhiteSpace(_environment.GetEnvironmentVariable("OPENAI_API_KEY")))
        {
            return new ProviderAuthResult(ProviderId, AuthenticationStatus.Authenticated, "Authenticated via OPENAI_API_KEY environment variable.", "codex login");
        }

        return new ProviderAuthResult(
            ProviderId,
            AuthenticationStatus.NotAuthenticated,
            "Not signed in to OpenAI Codex. Click Sign In to launch OpenAI's official login flow.",
            "codex login");
    }

    public async Task<OperationResult> BeginProviderLoginAsync(CancellationToken cancellationToken = default)
    {
        var detection = await DetectInstallationAsync(cancellationToken).ConfigureAwait(false);
        if (detection.Status != InstallationStatus.Installed || string.IsNullOrWhiteSpace(detection.ExecutablePath))
        {
            return OperationResult.Fail("OpenAI Codex CLI is not installed.", "CLI_NOT_FOUND", "Install Codex CLI from https://developers.openai.com/.");
        }

        return await _processRunner.LaunchInteractiveAsync(detection.ExecutablePath, new[] { "login" }, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<ProviderRegistrationState> GetRegistrationStateAsync(CancellationToken cancellationToken = default)
    {
        var configPath = GetUserConfigPath();
        return Task.FromResult(TomlMcpConfigHelper.InspectCodexServer(_fileSystem, configPath, ServerEntryName));
    }

    public async Task<OperationResult> InstallMcpRegistrationAsync(string pyRevitExecutablePath, int? revitYear = null, CancellationToken cancellationToken = default)
    {
        var pathCheck = PathValidator.ValidateLocalExecutablePath(pyRevitExecutablePath);
        if (!pathCheck.Succeeded)
        {
            return pathCheck;
        }

        var configPath = GetUserConfigPath();
        var backup = _backupService.CreateBackup(configPath, "codex");

        // Primary path: native pyRevit registration when non-elevated and no year override
        if (!_environment.IsElevated && !revitYear.HasValue)
        {
            var nativeRes = await _processRunner.RunAsync(
                pyRevitExecutablePath,
                new[] { "mcp", "install", "codex" },
                timeout: TimeSpan.FromSeconds(20),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (nativeRes.Succeeded)
            {
                var verifyState = await GetRegistrationStateAsync(cancellationToken).ConfigureAwait(false);
                if (verifyState.Status == RegistrationStatus.Registered)
                {
                    _logger.Info("CodexAdapter", "Registered pyRevit MCP with Codex via native 'pyrevit mcp install codex'.");
                    return OperationResult.Ok("Registered pyRevit MCP server with OpenAI Codex (user scope).", backup.BackupPath);
                }
            }
        }

        var args = revitYear.HasValue
            ? new[] { "mcp", $"--revit={revitYear.Value}" }
            : new[] { "mcp" };

        var upsert = TomlMcpConfigHelper.UpsertCodexServer(_fileSystem, _backupService, configPath, ServerEntryName, pyRevitExecutablePath, args);
        if (upsert.Succeeded)
        {
            _logger.Info("CodexAdapter", $"Registered pyRevit MCP with OpenAI Codex in '{configPath}'.");
        }

        return upsert;
    }

    public Task<OperationResult> RemoveMcpRegistrationAsync(CancellationToken cancellationToken = default)
    {
        var configPath = GetUserConfigPath();
        var res = TomlMcpConfigHelper.RemoveOwnedCodexServer(_fileSystem, _backupService, configPath, ServerEntryName);
        if (res.Succeeded)
        {
            _logger.Info("CodexAdapter", "Removed AV Orch pyRevit MCP registration from OpenAI Codex.");
        }

        return Task.FromResult(res);
    }

    public async Task<OperationResult> ValidateConnectionAsync(string pyRevitExecutablePath, CancellationToken cancellationToken = default)
    {
        var detection = await DetectInstallationAsync(cancellationToken).ConfigureAwait(false);
        if (detection.Status != InstallationStatus.Installed)
        {
            return OperationResult.Fail("OpenAI Codex CLI is not installed.", "CLI_NOT_FOUND");
        }

        var reg = await GetRegistrationStateAsync(cancellationToken).ConfigureAwait(false);
        if (reg.Status != RegistrationStatus.Registered || string.IsNullOrWhiteSpace(reg.RegisteredCommand))
        {
            return OperationResult.Fail($"OpenAI Codex MCP registration is not active ({reg.Status}).", "NOT_REGISTERED");
        }

        var probe = await _mcpProbe.ProbeInitializeAndToolsAsync(reg.RegisteredCommand, reg.RegisteredArgs, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!probe.Succeeded)
        {
            return probe;
        }

        return OperationResult.Ok("OpenAI Codex MCP registration and pyRevit stdio server validated.");
    }

    public async Task<OperationResult> RepairRegistrationAsync(string pyRevitExecutablePath, int? revitYear = null, CancellationToken cancellationToken = default)
    {
        var configPath = GetUserConfigPath();
        var state = await GetRegistrationStateAsync(cancellationToken).ConfigureAwait(false);
        if (state.Status == RegistrationStatus.CorruptConfig)
        {
            var backup = _backupService.CreateBackup(configPath, "codex");
            _fileSystem.WriteAllText(configPath, string.Empty);
            _logger.Warn("CodexAdapter", $"Backed up corrupt Codex config.toml to '{backup.BackupPath}' and initialized clean file.");
        }

        return await InstallMcpRegistrationAsync(pyRevitExecutablePath, revitYear, cancellationToken).ConfigureAwait(false);
    }
}
