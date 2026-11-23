using System.Text.RegularExpressions;
using AV.Orch.Core.Abstractions;
using AV.Orch.Core.Models;
using AV.Orch.Providers.Common;
using AV.Orch.Security;

namespace AV.Orch.Providers.Claude;

public sealed class ClaudeCodeAdapter : IAiProviderAdapter
{
    private static readonly Regex SemVerRegex = new(@"(?<ver>\d+\.\d+(?:\.\d+)?)", RegexOptions.Compiled);

    private readonly IProcessRunner _processRunner;
    private readonly IFileSystem _fileSystem;
    private readonly IEnvironmentProvider _environment;
    private readonly IConfigurationBackupService _backupService;
    private readonly IMcpStdioProbe _mcpProbe;
    private readonly IAppLogger _logger;

    public ProviderId ProviderId => ProviderId.ClaudeCode;
    public string DisplayName => "Anthropic Claude Code";
    public string VendorName => "Anthropic";
    public string ServerEntryName => "pyrevit";

    public ClaudeCodeAdapter(
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
        var customDir = _environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        return !string.IsNullOrWhiteSpace(customDir)
            ? Path.Combine(customDir, ".claude.json")
            : Path.Combine(_environment.UserProfileDirectory, ".claude.json");
    }

    public async Task<ProviderDetectionResult> DetectInstallationAsync(CancellationToken cancellationToken = default)
    {
        var exePath = await _processRunner.FindExecutableInPathAsync("claude", cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return new ProviderDetectionResult(
                ProviderId,
                InstallationStatus.NotInstalled,
                null,
                null,
                "Claude Code CLI ('claude') was not found on PATH.",
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
            $"Claude Code CLI {(version ?? "(version unknown)")} detected at '{exePath}'.",
            IsSupportedVersion: true);
    }

    public async Task<string?> GetInstalledVersionAsync(CancellationToken cancellationToken = default)
    {
        var res = await _processRunner.RunAsync("claude", new[] { "--version" }, timeout: TimeSpan.FromSeconds(10), cancellationToken: cancellationToken)
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
                "Install Claude Code CLI before checking authentication.",
                "claude auth login");
        }

        var res = await _processRunner.RunAsync(detection.ExecutablePath, new[] { "auth", "status" }, timeout: TimeSpan.FromSeconds(10), cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var combined = $"{res.StandardOutput}\n{res.StandardError}".ToLowerInvariant();
        if (combined.Contains("expired") || combined.Contains("session expired"))
        {
            return new ProviderAuthResult(ProviderId, AuthenticationStatus.Expired, "Claude Code session expired. Please sign in again.", "claude auth login");
        }

        if (res.Succeeded && (combined.Contains("logged in") || combined.Contains("authenticated") || combined.Contains("active") || combined.Contains("\"loggedin\":true")))
        {
            return new ProviderAuthResult(ProviderId, AuthenticationStatus.Authenticated, "Authenticated with Anthropic Claude Code.", "claude auth login");
        }

        if (!string.IsNullOrWhiteSpace(_environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")))
        {
            return new ProviderAuthResult(ProviderId, AuthenticationStatus.Authenticated, "Authenticated via ANTHROPIC_API_KEY environment variable.", "claude auth login");
        }

        return new ProviderAuthResult(
            ProviderId,
            AuthenticationStatus.NotAuthenticated,
            "Not signed in to Claude Code. Click Sign In to open Anthropic's official login flow.",
            "claude auth login");
    }

    public async Task<OperationResult> BeginProviderLoginAsync(CancellationToken cancellationToken = default)
    {
        var detection = await DetectInstallationAsync(cancellationToken).ConfigureAwait(false);
        if (detection.Status != InstallationStatus.Installed || string.IsNullOrWhiteSpace(detection.ExecutablePath))
        {
            return OperationResult.Fail("Claude Code CLI is not installed.", "CLI_NOT_FOUND", "Install Claude Code from https://code.claude.com/docs.");
        }

        return await _processRunner.LaunchInteractiveAsync(detection.ExecutablePath, new[] { "auth", "login" }, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<ProviderRegistrationState> GetRegistrationStateAsync(CancellationToken cancellationToken = default)
    {
        var configPath = GetUserConfigPath();
        var state = JsonMcpConfigHelper.InspectServerEntry(ProviderId, _fileSystem, configPath, ServerEntryName);
        return Task.FromResult(state);
    }

    public async Task<OperationResult> InstallMcpRegistrationAsync(string pyRevitExecutablePath, int? revitYear = null, CancellationToken cancellationToken = default)
    {
        var pathCheck = PathValidator.ValidateLocalExecutablePath(pyRevitExecutablePath);
        if (!pathCheck.Succeeded)
        {
            return pathCheck;
        }

        var configPath = GetUserConfigPath();
        var backup = _backupService.CreateBackup(configPath, "claude");

        // Primary path: native pyRevit registration when non-elevated and no specific Revit year override
        if (!_environment.IsElevated && !revitYear.HasValue)
        {
            var nativeRes = await _processRunner.RunAsync(
                pyRevitExecutablePath,
                new[] { "mcp", "install", "claude" },
                timeout: TimeSpan.FromSeconds(20),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (nativeRes.Succeeded)
            {
                var verifyState = await GetRegistrationStateAsync(cancellationToken).ConfigureAwait(false);
                if (verifyState.Status == RegistrationStatus.Registered)
                {
                    _logger.Info("ClaudeAdapter", "Registered pyRevit MCP with Claude Code via native 'pyrevit mcp install claude'.");
                    return OperationResult.Ok("Registered pyRevit MCP server with Claude Code (user scope).", backup.BackupPath);
                }
            }
        }

        // Surgical JSON fallback (preserves all existing MCP servers and settings)
        var args = revitYear.HasValue
            ? new[] { "mcp", $"--revit={revitYear.Value}" }
            : new[] { "mcp" };

        var upsertRes = JsonMcpConfigHelper.UpsertServerEntry(
            _fileSystem,
            _backupService,
            "claude",
            configPath,
            ServerEntryName,
            pyRevitExecutablePath,
            args,
            includeStdioType: true);

        if (upsertRes.Succeeded)
        {
            _logger.Info("ClaudeAdapter", $"Registered pyRevit MCP with Claude Code in '{configPath}'.");
        }

        return upsertRes;
    }

    public async Task<OperationResult> RemoveMcpRegistrationAsync(CancellationToken cancellationToken = default)
    {
        var configPath = GetUserConfigPath();
        var state = await GetRegistrationStateAsync(cancellationToken).ConfigureAwait(false);
        if (state.Status == RegistrationStatus.NotRegistered)
        {
            return OperationResult.Ok("Claude Code does not have a pyRevit MCP registration.");
        }

        if (!state.OwnedByAvOrch)
        {
            return OperationResult.Fail(
                "Refused to remove Claude Code 'pyrevit' entry because it is not owned by pyRevit/AV Orch.",
                "NOT_OWNED_BY_AVORCH");
        }

        // Clean surgical removal preserving all unrelated MCP servers and user settings
        var removeRes = JsonMcpConfigHelper.RemoveOwnedServerEntry(_fileSystem, _backupService, "claude", configPath, ServerEntryName);
        if (removeRes.Succeeded)
        {
            _logger.Info("ClaudeAdapter", "Removed AV Orch pyRevit MCP registration from Claude Code.");
        }

        return removeRes;
    }

    public async Task<OperationResult> ValidateConnectionAsync(string pyRevitExecutablePath, CancellationToken cancellationToken = default)
    {
        var detection = await DetectInstallationAsync(cancellationToken).ConfigureAwait(false);
        if (detection.Status != InstallationStatus.Installed)
        {
            return OperationResult.Fail("Claude Code CLI is not installed.", "CLI_NOT_FOUND");
        }

        var reg = await GetRegistrationStateAsync(cancellationToken).ConfigureAwait(false);
        if (reg.Status != RegistrationStatus.Registered || string.IsNullOrWhiteSpace(reg.RegisteredCommand))
        {
            return OperationResult.Fail($"Claude Code MCP registration is not active ({reg.Status}).", "NOT_REGISTERED");
        }

        var probe = await _mcpProbe.ProbeInitializeAndToolsAsync(reg.RegisteredCommand, reg.RegisteredArgs, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!probe.Succeeded)
        {
            return probe;
        }

        return OperationResult.Ok("Claude Code MCP registration and pyRevit stdio server validated.");
    }

    public async Task<OperationResult> RepairRegistrationAsync(string pyRevitExecutablePath, int? revitYear = null, CancellationToken cancellationToken = default)
    {
        var configPath = GetUserConfigPath();
        var state = await GetRegistrationStateAsync(cancellationToken).ConfigureAwait(false);
        if (state.Status == RegistrationStatus.CorruptConfig)
        {
            var backup = _backupService.CreateBackup(configPath, "claude");
            _fileSystem.WriteAllText(configPath, "{}" + Environment.NewLine);
            _logger.Warn("ClaudeAdapter", $"Backed up corrupt Claude config to '{backup.BackupPath}' and initialized clean JSON.");
        }

        return await InstallMcpRegistrationAsync(pyRevitExecutablePath, revitYear, cancellationToken).ConfigureAwait(false);
    }
}
