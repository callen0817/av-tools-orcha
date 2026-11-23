using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using AV.Orch.Core.Abstractions;
using AV.Orch.Core.Models;
using AV.Orch.Security;

namespace AV.Orch.Revit;

public sealed class PyRevitService : IPyRevitService
{
    private static readonly Version MinSupportedAgentVersion = new(7, 0, 0);
    private static readonly Regex SemVerRegex = new(@"(?<ver>\d+\.\d+(?:\.\d+)?)", RegexOptions.Compiled);

    private readonly IProcessRunner _processRunner;
    private readonly IFileSystem _fileSystem;
    private readonly IEnvironmentProvider _environment;
    private readonly IAppLogger _logger;

    public PyRevitService(
        IProcessRunner processRunner,
        IFileSystem fileSystem,
        IEnvironmentProvider environment,
        IAppLogger logger)
    {
        _processRunner = processRunner;
        _fileSystem = fileSystem;
        _environment = environment;
        _logger = logger;
    }

    public async Task<PyRevitInfo> DetectPyRevitAsync(string? customPath = null, CancellationToken cancellationToken = default)
    {
        var exePath = await ResolvePyRevitExecutableAsync(customPath, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return new PyRevitInfo(
                IsInstalled: false,
                ExecutablePath: null,
                Version: null,
                SupportsAgentRuntime: false,
                AgentHostEnabled: false,
                ConfiguredPolicy: null,
                DefaultEngine: null,
                RecklessModeEnabled: false,
                Message: "pyRevit CLI (pyrevit.exe) was not found on PATH or in standard installation directories.");
        }

        var pathCheck = PathValidator.ValidateLocalExecutablePath(exePath);
        if (!pathCheck.Succeeded)
        {
            return new PyRevitInfo(
                false, exePath, null, false, false, null, null, false,
                $"Rejected pyRevit executable path: {pathCheck.Message}");
        }

        var versionResult = await _processRunner.RunAsync(exePath, new[] { "--version" }, timeout: TimeSpan.FromSeconds(10), cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!versionResult.Succeeded)
        {
            return new PyRevitInfo(
                false, exePath, null, false, false, null, null, false,
                $"pyRevit executable failed to respond to --version: {versionResult.StandardError}");
        }

        var versionText = ExtractVersion(versionResult.StandardOutput);
        var supportsAgent = IsVersionAtLeast(versionText, MinSupportedAgentVersion);

        // Verify agent subcommand availability as well
        var agentEnabled = false;
        string? policy = null;
        string? engine = null;
        var reckless = false;

        if (supportsAgent)
        {
            var enabledRes = await _processRunner.RunAsync(exePath, new[] { "configs", "agent" }, timeout: TimeSpan.FromSeconds(10), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (enabledRes.Succeeded)
            {
                agentEnabled = ParseBooleanOutput(enabledRes.StandardOutput);
            }
            else
            {
                supportsAgent = false;
            }
        }

        if (supportsAgent)
        {
            var policyRes = await _processRunner.RunAsync(exePath, new[] { "configs", "agent", "policy" }, timeout: TimeSpan.FromSeconds(10), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (policyRes.Succeeded)
            {
                policy = ExtractLastToken(policyRes.StandardOutput);
            }

            var engineRes = await _processRunner.RunAsync(exePath, new[] { "configs", "agent", "engine" }, timeout: TimeSpan.FromSeconds(10), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (engineRes.Succeeded)
            {
                engine = ExtractLastToken(engineRes.StandardOutput);
            }

            var recklessRes = await _processRunner.RunAsync(exePath, new[] { "configs", "agent", "reckless" }, timeout: TimeSpan.FromSeconds(10), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (recklessRes.Succeeded)
            {
                reckless = ParseBooleanOutput(recklessRes.StandardOutput);
            }
        }

        var message = supportsAgent
            ? $"pyRevit {versionText ?? "7.x"} detected with MCP agent runtime ({(agentEnabled ? "enabled" : "disabled")})."
            : $"pyRevit {versionText ?? "unknown"} detected, but MCP agent runtime requires pyRevit 7.0+ (7.1+ recommended).";

        return new PyRevitInfo(
            IsInstalled: true,
            ExecutablePath: exePath,
            Version: versionText,
            SupportsAgentRuntime: supportsAgent,
            AgentHostEnabled: agentEnabled,
            ConfiguredPolicy: policy,
            DefaultEngine: engine,
            RecklessModeEnabled: reckless,
            Message: message);
    }

    public async Task<OperationResult> EnableAgentHostAsync(string pyRevitExePath, CancellationToken cancellationToken = default)
    {
        var pathCheck = PathValidator.ValidateLocalExecutablePath(pyRevitExePath);
        if (!pathCheck.Succeeded)
        {
            return pathCheck;
        }

        var result = await _processRunner.RunAsync(pyRevitExePath, new[] { "configs", "agent", "enable" }, timeout: TimeSpan.FromSeconds(15), cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            _logger.Error("PyRevit", $"Failed to enable agent host: {result.StandardError}");
            return OperationResult.Fail(
                $"Could not enable pyRevit agent host: {result.StandardError}",
                "PYREVIT_AGENT_ENABLE_FAILED",
                "Ensure pyRevit 7.0+ is installed and run 'pyrevit configs agent enable' from a standard terminal.");
        }

        _logger.Info("PyRevit", "Enabled pyRevit agent host.");
        return OperationResult.Ok("pyRevit agent host enabled. Reload pyRevit or restart Revit for changes to take effect.");
    }

    public async Task<OperationResult> DisableAgentHostAsync(string pyRevitExePath, CancellationToken cancellationToken = default)
    {
        var pathCheck = PathValidator.ValidateLocalExecutablePath(pyRevitExePath);
        if (!pathCheck.Succeeded)
        {
            return pathCheck;
        }

        var result = await _processRunner.RunAsync(pyRevitExePath, new[] { "configs", "agent", "disable" }, timeout: TimeSpan.FromSeconds(15), cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return OperationResult.Fail(
                $"Could not disable pyRevit agent host: {result.StandardError}",
                "PYREVIT_AGENT_DISABLE_FAILED");
        }

        _logger.Info("PyRevit", "Disabled pyRevit agent host.");
        return OperationResult.Ok("pyRevit agent host disabled.");
    }

    public async Task<OperationResult> SetAgentPolicyAsync(string pyRevitExePath, PermissionPolicy policy, CancellationToken cancellationToken = default)
    {
        var pathCheck = PathValidator.ValidateLocalExecutablePath(pyRevitExePath);
        if (!pathCheck.Succeeded)
        {
            return pathCheck;
        }

        var policyArg = PermissionPolicyGuard.ToPyRevitPolicyArgument(policy);
        var result = await _processRunner.RunAsync(
            pyRevitExePath,
            new[] { "configs", "agent", "policy", policyArg },
            timeout: TimeSpan.FromSeconds(15),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return OperationResult.Fail(
                $"Failed to set pyRevit agent policy to '{policyArg}': {result.StandardError}",
                "PYREVIT_POLICY_FAILED");
        }

        _logger.Info("PyRevit", $"Updated pyRevit agent policy to '{policyArg}'.");
        return OperationResult.Ok($"pyRevit agent policy set to '{policyArg}'.");
    }

    public async Task<IReadOnlyList<RevitSession>> GetRunningSessionsAsync(string pyRevitExePath, CancellationToken cancellationToken = default)
    {
        var pathCheck = PathValidator.ValidateLocalExecutablePath(pyRevitExePath);
        if (!pathCheck.Succeeded)
        {
            return Array.Empty<RevitSession>();
        }

        var result = await _processRunner.RunAsync(
            pyRevitExePath,
            new[] { "agent", "status", "--json" },
            timeout: TimeSpan.FromSeconds(10),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return Array.Empty<RevitSession>();
        }

        return ParseSessionsJson(result.StandardOutput);
    }

    public async Task<RevitDiagnosticResult> RunReadOnlyDiagnosticQueryAsync(string pyRevitExePath, int? revitYear = null, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var pathCheck = PathValidator.ValidateLocalExecutablePath(pyRevitExePath);
        if (!pathCheck.Succeeded)
        {
            return new RevitDiagnosticResult(false, pathCheck.Message, null, null, pathCheck.ErrorCode, pathCheck.RecoveryAction, sw.Elapsed);
        }

        var sessions = await GetRunningSessionsAsync(pyRevitExePath, cancellationToken).ConfigureAwait(false);
        if (sessions.Count == 0)
        {
            return new RevitDiagnosticResult(
                false,
                "No running Revit session with an active pyRevit agent host was found.",
                null,
                null,
                "REVIT_NOT_RUNNING",
                "Start Autodesk Revit as a normal user, ensure pyRevit agent runtime is enabled, and open a project.",
                sw.Elapsed);
        }

        // Uses pyRevit's fixed restricted 'agent context' command (no arbitrary Python execution!)
        var args = revitYear.HasValue
            ? new[] { "agent", "context", $"--revit={revitYear.Value}" }
            : new[] { "agent", "context" };

        var ctxResult = await _processRunner.RunAsync(pyRevitExePath, args, timeout: TimeSpan.FromSeconds(15), cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        sw.Stop();

        if (ctxResult.TimedOut)
        {
            return new RevitDiagnosticResult(
                false,
                "Timed out waiting for Revit agent host context response.",
                sessions[0],
                sessions[0].ActiveDocumentTitle,
                "CONNECTION_TIMEOUT",
                "Check if Autodesk Revit is busy or showing a modal dialog, then retry.",
                sw.Elapsed);
        }

        if (!ctxResult.Succeeded)
        {
            var errText = string.IsNullOrWhiteSpace(ctxResult.StandardError) ? ctxResult.StandardOutput : ctxResult.StandardError;
            return new RevitDiagnosticResult(
                false,
                $"Read-only Revit context query failed: {errText}",
                sessions[0],
                sessions[0].ActiveDocumentTitle,
                "REVIT_CONTEXT_FAILED",
                "Verify pyRevit agent runtime is enabled and reload pyRevit inside Revit.",
                sw.Elapsed);
        }

        var parsedSession = ParseContextJson(ctxResult.StandardOutput, sessions[0]);
        if (string.IsNullOrWhiteSpace(parsedSession.ActiveDocumentTitle))
        {
            return new RevitDiagnosticResult(
                true,
                $"Connected to Revit {parsedSession.RevitYear?.ToString() ?? "session"} (PID {parsedSession.ProcessId}), but no document is currently open.",
                parsedSession,
                null,
                "NO_ACTIVE_DOCUMENT",
                "Open a Revit project or family document to query model elements.",
                sw.Elapsed);
        }

        return new RevitDiagnosticResult(
            true,
            $"Read-only connection verified for '{parsedSession.ActiveDocumentTitle}' in Revit {parsedSession.RevitYear?.ToString() ?? "session"} (policy: {parsedSession.AgentPolicy ?? "unknown"}).",
            parsedSession,
            parsedSession.ActiveDocumentTitle,
            null,
            null,
            sw.Elapsed);
    }

    private async Task<string?> ResolvePyRevitExecutableAsync(string? customPath, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(customPath) && _fileSystem.FileExists(customPath))
        {
            return customPath;
        }

        var inPath = await _processRunner.FindExecutableInPathAsync("pyrevit", cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(inPath))
        {
            return inPath;
        }

        var candidates = new[]
        {
            Path.Combine(_environment.ProgramFilesDirectory, "pyRevit-Master", "bin", "pyrevit.exe"),
            Path.Combine(_environment.ProgramFilesDirectory, "pyRevitCLI", "pyrevit.exe"),
            Path.Combine(_environment.RoamingAppDataDirectory, "pyRevit-Master", "bin", "pyrevit.exe"),
            Path.Combine(_environment.LocalAppDataDirectory, "Programs", "pyRevit", "bin", "pyrevit.exe")
        };

        return candidates.FirstOrDefault(_fileSystem.FileExists);
    }

    public static IReadOnlyList<RevitSession> ParseSessionsJson(string json)
    {
        var sessions = new List<RevitSession>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            JsonElement arrayElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                arrayElement = root;
            }
            else if (root.ValueKind == JsonValueKind.Object &&
                     (root.TryGetProperty("instances", out arrayElement) ||
                      root.TryGetProperty("sessions", out arrayElement)))
            {
                if (arrayElement.ValueKind != JsonValueKind.Array)
                {
                    return sessions;
                }
            }
            else
            {
                return sessions;
            }

            foreach (var item in arrayElement.EnumerateArray())
            {
                var pid = TryGetInt(item, "pid", "process_id", "processId") ?? 0;
                var year = TryGetInt(item, "revit_year", "year", "version");
                var responding = TryGetBool(item, "responding", "connected") ?? true;
                var busy = TryGetBool(item, "busy") ?? false;
                var elevated = TryGetBool(item, "elevated", "is_admin") ?? false;
                var docTitle = TryGetString(item, "document", "active_document", "document_title");
                var docPath = TryGetString(item, "document_path", "path");
                var clone = TryGetString(item, "clone");
                var engine = TryGetString(item, "engine");
                var policy = TryGetString(item, "policy");

                sessions.Add(new RevitSession(
                    pid,
                    year,
                    docTitle,
                    docPath,
                    string.IsNullOrWhiteSpace(docTitle) ? Array.Empty<string>() : new[] { docTitle },
                    responding,
                    busy,
                    elevated,
                    clone,
                    engine,
                    policy));
            }
        }
        catch (JsonException)
        {
            // Non-JSON or malformed output -> return empty list
        }

        return sessions;
    }

    public static RevitSession ParseContextJson(string json, RevitSession fallback)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string? docTitle = fallback.ActiveDocumentTitle;
            string? docPath = fallback.ActiveDocumentPath;
            var openDocs = new List<string>();

            if (root.TryGetProperty("document", out var docEl))
            {
                if (docEl.ValueKind == JsonValueKind.String)
                {
                    docTitle = docEl.GetString();
                }
                else if (docEl.ValueKind == JsonValueKind.Object)
                {
                    docTitle = TryGetString(docEl, "title", "name") ?? docTitle;
                    docPath = TryGetString(docEl, "path") ?? docPath;
                }
            }

            if (root.TryGetProperty("open_documents", out var openEl) && openEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in openEl.EnumerateArray())
                {
                    var name = d.ValueKind == JsonValueKind.String ? d.GetString() : TryGetString(d, "title", "name");
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        openDocs.Add(name);
                    }
                }
            }

            int? year = fallback.RevitYear;
            if (root.TryGetProperty("revit", out var revitEl) && revitEl.ValueKind == JsonValueKind.Object)
            {
                year = TryGetInt(revitEl, "year", "version") ?? year;
            }

            string? policy = fallback.AgentPolicy;
            if (root.TryGetProperty("agent", out var agentEl) && agentEl.ValueKind == JsonValueKind.Object)
            {
                policy = TryGetString(agentEl, "policy") ?? policy;
            }

            string? engine = fallback.ScriptEngine;
            if (root.TryGetProperty("scripting", out var scriptEl) && scriptEl.ValueKind == JsonValueKind.Object)
            {
                engine = TryGetString(scriptEl, "default_engine") ?? engine;
            }

            return fallback with
            {
                RevitYear = year,
                ActiveDocumentTitle = docTitle,
                ActiveDocumentPath = docPath,
                OpenDocuments = openDocs.Count > 0 ? openDocs : fallback.OpenDocuments,
                AgentPolicy = policy,
                ScriptEngine = engine
            };
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    private static string? ExtractVersion(string output)
    {
        var match = SemVerRegex.Match(output ?? string.Empty);
        return match.Success ? match.Groups["ver"].Value : null;
    }

    private static bool IsVersionAtLeast(string? versionText, Version minimum)
    {
        if (string.IsNullOrWhiteSpace(versionText))
        {
            return false;
        }

        return Version.TryParse(versionText, out var parsed) && parsed >= minimum;
    }

    private static bool ParseBooleanOutput(string output)
    {
        var lower = (output ?? string.Empty).ToLowerInvariant();
        return lower.Contains("true") || lower.Contains("enabled") || lower.Contains("yes");
    }

    private static string? ExtractLastToken(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var tokens = output.Split(new[] { ' ', '\t', '\r', '\n', ':', '=' }, StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length > 0 ? tokens[^1].Trim() : null;
    }

    private static string? TryGetString(JsonElement obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (obj.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString();
            }
        }

        return null;
    }

    private static int? TryGetInt(JsonElement obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (!obj.TryGetProperty(name, out var prop))
            {
                continue;
            }

            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var val))
            {
                return val;
            }

            if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static bool? TryGetBool(JsonElement obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (obj.TryGetProperty(name, out var prop) &&
                (prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False))
            {
                return prop.GetBoolean();
            }
        }

        return null;
    }
}
