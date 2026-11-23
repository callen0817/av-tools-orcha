using System.Text;
using System.Text.RegularExpressions;
using AV.Orch.Core.Abstractions;
using AV.Orch.Core.Models;

namespace AV.Orch.Providers.Common;

public static class TomlMcpConfigHelper
{
    private static readonly Regex TableHeaderRegex = new(@"^\s*\[(?<header>[^\]]+)\]\s*(?:#.*)?$", RegexOptions.Compiled);
    private static readonly Regex KeyValueRegex = new(@"^\s*(?<key>[A-Za-z0-9_\-]+)\s*=\s*(?<val>.+?)\s*$", RegexOptions.Compiled);

    public static ProviderRegistrationState InspectCodexServer(
        IFileSystem fileSystem,
        string configPath,
        string serverKey)
    {
        if (!fileSystem.FileExists(configPath))
        {
            return new ProviderRegistrationState(
                ProviderId.Codex,
                RegistrationStatus.NotRegistered,
                configPath,
                null,
                Array.Empty<string>(),
                OwnedByAvOrch: false,
                Message: "Codex config.toml not found; pyRevit MCP server is not registered.",
                OtherServerCount: 0);
        }

        var content = fileSystem.ReadAllText(configPath);
        if (!ValidateBasicToml(content, out var validationError))
        {
            return new ProviderRegistrationState(
                ProviderId.Codex,
                RegistrationStatus.CorruptConfig,
                configPath,
                null,
                Array.Empty<string>(),
                OwnedByAvOrch: false,
                Message: validationError ?? "Codex config.toml has invalid TOML syntax.");
        }

        var targetHeader = $"mcp_servers.{serverKey}";
        var lines = SplitLines(content);
        string? currentHeader = null;
        string? command = null;
        var args = new List<string>();
        var foundTarget = false;
        var otherServers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            var headerMatch = TableHeaderRegex.Match(line);
            if (headerMatch.Success)
            {
                currentHeader = headerMatch.Groups["header"].Value.Trim();
                if (currentHeader.StartsWith("mcp_servers.", StringComparison.OrdinalIgnoreCase))
                {
                    var name = currentHeader["mcp_servers.".Length..].Split('.')[0];
                    if (string.Equals(name, serverKey, StringComparison.OrdinalIgnoreCase))
                    {
                        foundTarget = true;
                    }
                    else
                    {
                        otherServers.Add(name);
                    }
                }

                continue;
            }

            if (string.Equals(currentHeader, targetHeader, StringComparison.OrdinalIgnoreCase))
            {
                var kvMatch = KeyValueRegex.Match(line);
                if (!kvMatch.Success)
                {
                    continue;
                }

                var key = kvMatch.Groups["key"].Value;
                var val = StripInlineComment(kvMatch.Groups["val"].Value);

                if (string.Equals(key, "command", StringComparison.OrdinalIgnoreCase))
                {
                    command = UnquoteTomlString(val);
                }
                else if (string.Equals(key, "args", StringComparison.OrdinalIgnoreCase))
                {
                    args = ParseTomlStringArray(val);
                }
            }
        }

        if (!foundTarget)
        {
            return new ProviderRegistrationState(
                ProviderId.Codex,
                RegistrationStatus.NotRegistered,
                configPath,
                null,
                Array.Empty<string>(),
                OwnedByAvOrch: false,
                Message: "pyRevit MCP server is not registered in Codex config.toml.",
                OtherServerCount: otherServers.Count);
        }

        if (!JsonMcpConfigHelper.IsPyRevitMcpRegistration(command, args))
        {
            return new ProviderRegistrationState(
                ProviderId.Codex,
                RegistrationStatus.Conflicting,
                configPath,
                command,
                args,
                OwnedByAvOrch: false,
                Message: $"[mcp_servers.{serverKey}] exists in Codex config.toml but points to '{command}'.",
                OtherServerCount: otherServers.Count);
        }

        return new ProviderRegistrationState(
            ProviderId.Codex,
            RegistrationStatus.Registered,
            configPath,
            command,
            args,
            OwnedByAvOrch: true,
            Message: $"Registered [mcp_servers.{serverKey}] in Codex config.toml.",
            OtherServerCount: otherServers.Count);
    }

    public static OperationResult UpsertCodexServer(
        IFileSystem fileSystem,
        IConfigurationBackupService backupService,
        string configPath,
        string serverKey,
        string command,
        IReadOnlyList<string> args)
    {
        var backup = backupService.CreateBackup(configPath, "codex");
        var existing = fileSystem.FileExists(configPath) ? fileSystem.ReadAllText(configPath) : string.Empty;

        if (!string.IsNullOrWhiteSpace(existing) && !ValidateBasicToml(existing, out var syntaxErr))
        {
            return OperationResult.Fail(
                $"Cannot update corrupt Codex config.toml: {syntaxErr}",
                "CORRUPT_CONFIG",
                "Repair or restore a previous Codex config backup.",
                backup.BackupPath);
        }

        var cleaned = RemoveTableSection(existing, $"mcp_servers.{serverKey}");
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(cleaned))
        {
            sb.Append(cleaned.TrimEnd());
            sb.AppendLine();
            sb.AppendLine();
        }

        sb.AppendLine($"[mcp_servers.{serverKey}]");
        sb.AppendLine($"command = {QuoteTomlString(command)}");
        sb.AppendLine($"args = [{string.Join(", ", args.Select(QuoteTomlString))}]");

        var updated = sb.ToString();
        if (!ValidateBasicToml(updated, out var postErr))
        {
            return OperationResult.Fail($"Generated TOML failed validation: {postErr}", "TOML_VALIDATION_FAILED", null, backup.BackupPath);
        }

        try
        {
            fileSystem.WriteAllText(configPath, updated);
            return OperationResult.Ok($"Registered [mcp_servers.{serverKey}] in '{configPath}'.", backup.BackupPath);
        }
        catch (Exception ex)
        {
            backupService.RestoreBackup(backup.BackupPath, configPath);
            return OperationResult.Fail($"Failed to write Codex config.toml (restored backup): {ex.Message}", "CONFIG_WRITE_FAILED", null, backup.BackupPath);
        }
    }

    public static OperationResult RemoveOwnedCodexServer(
        IFileSystem fileSystem,
        IConfigurationBackupService backupService,
        string configPath,
        string serverKey)
    {
        if (!fileSystem.FileExists(configPath))
        {
            return OperationResult.Ok("Codex config.toml does not exist; nothing to remove.");
        }

        var state = InspectCodexServer(fileSystem, configPath, serverKey);
        if (state.Status == RegistrationStatus.NotRegistered)
        {
            return OperationResult.Ok("No pyRevit registration found in Codex config.toml.");
        }

        if (!state.OwnedByAvOrch)
        {
            return OperationResult.Fail(
                $"Refused to remove [mcp_servers.{serverKey}] because it is not owned by pyRevit/AV Orch.",
                "NOT_OWNED_BY_AVORCH");
        }

        var backup = backupService.CreateBackup(configPath, "codex");
        var existing = fileSystem.ReadAllText(configPath);
        var updated = RemoveTableSection(existing, $"mcp_servers.{serverKey}");

        try
        {
            fileSystem.WriteAllText(configPath, updated);
            return OperationResult.Ok($"Removed [mcp_servers.{serverKey}] from Codex config.toml.", backup.BackupPath);
        }
        catch (Exception ex)
        {
            backupService.RestoreBackup(backup.BackupPath, configPath);
            return OperationResult.Fail($"Failed to write Codex config.toml (restored backup): {ex.Message}", "CONFIG_WRITE_FAILED", null, backup.BackupPath);
        }
    }

    private static string RemoveTableSection(string content, string targetHeader)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        var lines = SplitLines(content);
        var kept = new List<string>();
        var skipping = false;

        foreach (var line in lines)
        {
            var headerMatch = TableHeaderRegex.Match(line);
            if (headerMatch.Success)
            {
                var header = headerMatch.Groups["header"].Value.Trim();
                skipping = string.Equals(header, targetHeader, StringComparison.OrdinalIgnoreCase);
            }

            if (!skipping)
            {
                kept.Add(line);
            }
        }

        return string.Join(Environment.NewLine, kept).TrimEnd() + (kept.Count > 0 ? Environment.NewLine : string.Empty);
    }

    private static bool ValidateBasicToml(string content, out string? error)
    {
        error = null;
        var lines = SplitLines(content);
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
            {
                continue;
            }

            if (trimmed.StartsWith('['))
            {
                if (!TableHeaderRegex.IsMatch(trimmed))
                {
                    error = $"Malformed TOML table header on line {i + 1}.";
                    return false;
                }

                continue;
            }

            if (!trimmed.Contains('='))
            {
                error = $"Expected key = value pair on line {i + 1}.";
                return false;
            }
        }

        return true;
    }

    private static string[] SplitLines(string content) =>
        content.Replace("\r\n", "\n").Split('\n');

    private static string QuoteTomlString(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string UnquoteTomlString(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            return trimmed[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        if (trimmed.Length >= 2 && trimmed[0] == '\'' && trimmed[^1] == '\'')
        {
            return trimmed[1..^1];
        }

        return trimmed;
    }

    private static List<string> ParseTomlStringArray(string value)
    {
        var result = new List<string>();
        var trimmed = value.Trim();
        if (!trimmed.StartsWith('[') || !trimmed.EndsWith(']'))
        {
            return result;
        }

        var inner = trimmed[1..^1];
        foreach (Match m in Regex.Matches(inner, @"""(?:\\.|[^""])*""|'(?:[^'])*'"))
        {
            result.Add(UnquoteTomlString(m.Value));
        }

        return result;
    }

    private static string StripInlineComment(string val)
    {
        var inDouble = false;
        var inSingle = false;
        for (var i = 0; i < val.Length; i++)
        {
            var c = val[i];
            if (c == '"' && !inSingle && (i == 0 || val[i - 1] != '\\'))
            {
                inDouble = !inDouble;
            }
            else if (c == '\'' && !inDouble)
            {
                inSingle = !inSingle;
            }
            else if (c == '#' && !inDouble && !inSingle)
            {
                return val[..i].Trim();
            }
        }

        return val.Trim();
    }
}
