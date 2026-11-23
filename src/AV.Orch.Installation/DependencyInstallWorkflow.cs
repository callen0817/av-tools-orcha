using AV.Orch.Core.Abstractions;
using AV.Orch.Core.Models;

namespace AV.Orch.Installation;

public sealed record DependencyInstallDescriptor(
    string Id,
    string DisplayName,
    string OfficialSourceUrl,
    string RequiredPermissions,
    string ManualInstallInstructions,
    string? WingetPackageId = null,
    string? NpmPackageName = null);

public sealed class DependencyInstallWorkflow
{
    private readonly IProcessRunner _processRunner;
    private readonly IAuditLogService _auditLog;

    public DependencyInstallWorkflow(IProcessRunner processRunner, IAuditLogService auditLog)
    {
        _processRunner = processRunner;
        _auditLog = auditLog;
    }

    public static IReadOnlyList<DependencyInstallDescriptor> GetOfficialDescriptors() => new[]
    {
        new DependencyInstallDescriptor(
            "pyrevit",
            "pyRevit 7.1+ (Official Release)",
            "https://github.com/pyrevitlabs/pyRevit/releases",
            "Standard user (per-user installer) or Administrator (all-users installer).",
            "Download the official pyRevit 7.1+ installer from GitHub Releases, run it, then run 'pyrevit configs agent enable'."),
        new DependencyInstallDescriptor(
            "codex",
            "OpenAI Codex CLI",
            "https://developers.openai.com/",
            "Standard user (npm global package or official installer).",
            "Install via official OpenAI Codex CLI instructions (e.g. 'npm install -g @openai/codex'), then run 'codex login'.",
            NpmPackageName: "@openai/codex"),
        new DependencyInstallDescriptor(
            "gemini",
            "Google Gemini CLI",
            "https://geminicli.com/docs/get-started/",
            "Standard user (Node.js 20+ and npm).",
            "Install via 'npm install -g @google/gemini-cli', then run 'gemini' to sign in with your Google account.",
            NpmPackageName: "@google/gemini-cli"),
        new DependencyInstallDescriptor(
            "claude",
            "Anthropic Claude Code",
            "https://code.claude.com/docs/en/overview",
            "Standard user (native installer or npm).",
            "Install Claude Code following https://code.claude.com/docs, then run 'claude auth login'.",
            NpmPackageName: "@anthropic-ai/claude-code")
    };

    public async Task<OperationResult> LaunchOfficialDownloadPageAsync(
        DependencyInstallDescriptor descriptor,
        bool userApproved,
        CancellationToken cancellationToken = default)
    {
        if (!userApproved)
        {
            return OperationResult.Fail(
                $"Explicit user approval is required before opening or installing '{descriptor.DisplayName}'.",
                "APPROVAL_REQUIRED");
        }

        var result = await _processRunner.LaunchInteractiveAsync(descriptor.OfficialSourceUrl, Array.Empty<string>(), cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        _auditLog.Record("OpenOfficialInstallerSource", "User", descriptor.Id, result.Succeeded,
            $"Opened official source {descriptor.OfficialSourceUrl} for {descriptor.DisplayName}.");
        return result;
    }

    public async Task<OperationResult> InstallNpmCliWithApprovalAsync(
        DependencyInstallDescriptor descriptor,
        bool userApproved,
        CancellationToken cancellationToken = default)
    {
        if (!userApproved)
        {
            return OperationResult.Fail(
                $"Explicit user approval is required before installing '{descriptor.DisplayName}'.",
                "APPROVAL_REQUIRED");
        }

        if (string.IsNullOrWhiteSpace(descriptor.NpmPackageName))
        {
            return OperationResult.Fail(
                $"'{descriptor.DisplayName}' does not have an automated npm package install. Use the official installer at {descriptor.OfficialSourceUrl}.",
                "MANUAL_INSTALL_REQUIRED",
                descriptor.ManualInstallInstructions);
        }

        var npmPath = await _processRunner.FindExecutableInPathAsync("npm", cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(npmPath))
        {
            return OperationResult.Fail(
                "npm was not found on PATH.",
                "NPM_NOT_FOUND",
                $"Install Node.js LTS or follow manual instructions: {descriptor.ManualInstallInstructions}");
        }

        var res = await _processRunner.RunAsync(
            npmPath,
            new[] { "install", "-g", descriptor.NpmPackageName },
            timeout: TimeSpan.FromMinutes(3),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        _auditLog.Record("InstallDependency", "User", descriptor.Id, res.Succeeded,
            res.Succeeded ? $"Installed {descriptor.NpmPackageName}." : $"Failed to install {descriptor.NpmPackageName}: {res.StandardError}");

        return res.Succeeded
            ? OperationResult.Ok($"Installed {descriptor.DisplayName} ({descriptor.NpmPackageName}).")
            : OperationResult.Fail(
                $"Failed to install {descriptor.DisplayName}: {res.StandardError}",
                "DEPENDENCY_INSTALL_FAILED",
                descriptor.ManualInstallInstructions);
    }
}
