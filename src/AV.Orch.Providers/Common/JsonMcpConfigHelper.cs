using System.Text.Json;
using System.Text.Json.Nodes;
using AV.Orch.Core.Abstractions;
using AV.Orch.Core.Models;

namespace AV.Orch.Providers.Common;

public static class JsonMcpConfigHelper
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
    };

    public static (bool IsValid, JsonObject? Root, string? Error) TryLoadRoot(IFileSystem fileSystem, string configPath)
    {
        if (!fileSystem.FileExists(configPath))
        {
            return (true, new JsonObject(), null);
        }

        try
        {
            var raw = fileSystem.ReadAllText(configPath);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return (true, new JsonObject(), null);
            }

            var node = JsonNode.Parse(raw);
            if (node is not JsonObject rootObj)
            {
                return (false, null, "Configuration root must be a JSON object.");
            }

            return (true, rootObj, null);
        }
        catch (JsonException ex)
        {
            return (false, null, $"Corrupt JSON configuration: {ex.Message}");
        }
    }

    public static ProviderRegistrationState InspectServerEntry(
        ProviderId providerId,
        IFileSystem fileSystem,
        string configPath,
        string serverKey)
    {
        var (isValid, root, error) = TryLoadRoot(fileSystem, configPath);
        if (!isValid || root is null)
        {
            return new ProviderRegistrationState(
                providerId,
                RegistrationStatus.CorruptConfig,
                configPath,
                null,
                Array.Empty<string>(),
                OwnedByAvOrch: false,
                Message: error ?? "Configuration file is corrupt.");
        }

        if (root["mcpServers"] is not JsonObject servers)
        {
            return new ProviderRegistrationState(
                providerId,
                RegistrationStatus.NotRegistered,
                configPath,
                null,
                Array.Empty<string>(),
                OwnedByAvOrch: false,
                Message: "pyRevit MCP server is not registered.",
                OtherServerCount: 0);
        }

        var otherCount = servers.Count(kv => !string.Equals(kv.Key, serverKey, StringComparison.OrdinalIgnoreCase));
        var matchingEntry = servers.FirstOrDefault(kv => string.Equals(kv.Key, serverKey, StringComparison.OrdinalIgnoreCase));
        if (matchingEntry.Value is not JsonObject entryObj)
        {
            return new ProviderRegistrationState(
                providerId,
                RegistrationStatus.NotRegistered,
                configPath,
                null,
                Array.Empty<string>(),
                OwnedByAvOrch: false,
                Message: "pyRevit MCP server is not registered.",
                OtherServerCount: otherCount);
        }

        var command = entryObj["command"]?.GetValue<string>();
        var args = new List<string>();
        if (entryObj["args"] is JsonArray argsArray)
        {
            foreach (var item in argsArray)
            {
                var str = item?.GetValue<string>();
                if (str is not null)
                {
                    args.Add(str);
                }
            }
        }

        var isPyRevitCommand = IsPyRevitMcpRegistration(command, args);
        if (!isPyRevitCommand)
        {
            return new ProviderRegistrationState(
                providerId,
                RegistrationStatus.Conflicting,
                configPath,
                command,
                args,
                OwnedByAvOrch: false,
                Message: $"Entry '{serverKey}' exists but points to an unrelated command ('{command}').",
                OtherServerCount: otherCount);
        }

        return new ProviderRegistrationState(
            providerId,
            RegistrationStatus.Registered,
            configPath,
            command,
            args,
            OwnedByAvOrch: true,
            Message: $"Registered '{serverKey}' MCP server ({command}).",
            OtherServerCount: otherCount);
    }

    public static OperationResult UpsertServerEntry(
        IFileSystem fileSystem,
        IConfigurationBackupService backupService,
        string backupFolder,
        string configPath,
        string serverKey,
        string command,
        IReadOnlyList<string> args,
        bool includeStdioType = false,
        bool includeTrustFalse = false)
    {
        var backup = backupService.CreateBackup(configPath, backupFolder);

        var (isValid, root, error) = TryLoadRoot(fileSystem, configPath);
        if (!isValid || root is null)
        {
            return OperationResult.Fail(
                $"Cannot update corrupt JSON configuration at '{configPath}': {error}",
                "CORRUPT_CONFIG",
                "Use Repair to reset or restore a previous backup.",
                backup.BackupPath);
        }

        var servers = root["mcpServers"] as JsonObject;
        if (servers is null)
        {
            servers = new JsonObject();
            root["mcpServers"] = servers;
        }

        var serverObj = new JsonObject();
        if (includeStdioType)
        {
            serverObj["type"] = "stdio";
        }

        serverObj["command"] = command;
        var argsNode = new JsonArray();
        foreach (var arg in args)
        {
            argsNode.Add(arg);
        }

        serverObj["args"] = argsNode;

        if (includeTrustFalse)
        {
            serverObj["trust"] = false;
        }

        servers[serverKey] = serverObj;

        try
        {
            var serialized = root.ToJsonString(WriteOptions);
            // Validate serialized JSON before write
            using var _ = JsonDocument.Parse(serialized);
            fileSystem.WriteAllText(configPath, serialized);

            // Verify post-write readability
            var verify = TryLoadRoot(fileSystem, configPath);
            if (!verify.IsValid)
            {
                backupService.RestoreBackup(backup.BackupPath, configPath);
                return OperationResult.Fail("Post-write configuration validation failed; restored backup.", "CONFIG_VERIFY_FAILED", null, backup.BackupPath);
            }

            return OperationResult.Ok($"Registered '{serverKey}' in '{configPath}'.", backup.BackupPath);
        }
        catch (Exception ex)
        {
            backupService.RestoreBackup(backup.BackupPath, configPath);
            return OperationResult.Fail(
                $"Failed to write configuration (restored backup): {ex.Message}",
                "CONFIG_WRITE_FAILED",
                null,
                backup.BackupPath);
        }
    }

    public static OperationResult RemoveOwnedServerEntry(
        IFileSystem fileSystem,
        IConfigurationBackupService backupService,
        string backupFolder,
        string configPath,
        string serverKey)
    {
        if (!fileSystem.FileExists(configPath))
        {
            return OperationResult.Ok("Configuration file does not exist; nothing to remove.");
        }

        var backup = backupService.CreateBackup(configPath, backupFolder);
        var (isValid, root, error) = TryLoadRoot(fileSystem, configPath);
        if (!isValid || root is null)
        {
            return OperationResult.Fail($"Cannot modify corrupt JSON config: {error}", "CORRUPT_CONFIG", null, backup.BackupPath);
        }

        if (root["mcpServers"] is not JsonObject servers || !servers.ContainsKey(serverKey))
        {
            return OperationResult.Ok($"No '{serverKey}' registration found; configuration unchanged.", backup.BackupPath);
        }

        if (servers[serverKey] is JsonObject entryObj)
        {
            var command = entryObj["command"]?.GetValue<string>();
            var args = entryObj["args"] is JsonArray arr
                ? arr.Select(n => n?.GetValue<string>() ?? string.Empty).ToList()
                : new List<string>();

            if (!IsPyRevitMcpRegistration(command, args))
            {
                return OperationResult.Fail(
                    $"Refused to remove '{serverKey}' because it does not belong to pyRevit/AV Orch.",
                    "NOT_OWNED_BY_AVORCH",
                    null,
                    backup.BackupPath);
            }
        }

        servers.Remove(serverKey);

        try
        {
            var serialized = root.ToJsonString(WriteOptions);
            using var _ = JsonDocument.Parse(serialized);
            fileSystem.WriteAllText(configPath, serialized);
            return OperationResult.Ok($"Removed AV Orch '{serverKey}' MCP registration while preserving other servers.", backup.BackupPath);
        }
        catch (Exception ex)
        {
            backupService.RestoreBackup(backup.BackupPath, configPath);
            return OperationResult.Fail($"Failed to save config after removal (restored backup): {ex.Message}", "CONFIG_WRITE_FAILED", null, backup.BackupPath);
        }
    }

    public static bool IsPyRevitMcpRegistration(string? command, IReadOnlyList<string> args)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        var exeName = Path.GetFileName(command);
        var isPyRevitExe = exeName.Contains("pyrevit", StringComparison.OrdinalIgnoreCase);
        var hasMcpArg = args.Any(a => string.Equals(a, "mcp", StringComparison.OrdinalIgnoreCase));
        return isPyRevitExe && hasMcpArg;
    }
}
