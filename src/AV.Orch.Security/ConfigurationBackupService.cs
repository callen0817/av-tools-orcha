using System.Security.Cryptography;
using System.Text;
using AV.Orch.Core.Abstractions;
using AV.Orch.Core.Models;

namespace AV.Orch.Security;

public sealed class ConfigurationBackupService : IConfigurationBackupService
{
    private readonly IFileSystem _fileSystem;
    private readonly string _backupRoot;

    public ConfigurationBackupService(IFileSystem fileSystem, IEnvironmentProvider environment)
    {
        _fileSystem = fileSystem;
        _backupRoot = Path.Combine(environment.LocalAppDataDirectory, "ArtificialVision", "AVOrch", "backups");
    }

    public ConfigBackupRecord CreateBackup(string filePath, string backupSubfolder)
    {
        var timestamp = DateTimeOffset.UtcNow;
        var safeFolder = Path.Combine(_backupRoot, SanitizeName(backupSubfolder));
        _fileSystem.CreateDirectory(safeFolder);

        var fileName = Path.GetFileName(filePath);
        var backupFileName = $"{fileName}.{timestamp:yyyyMMddHHmmssfff}.bak";
        var backupPath = Path.Combine(safeFolder, backupFileName);

        var content = _fileSystem.FileExists(filePath) ? _fileSystem.ReadAllText(filePath) : string.Empty;
        _fileSystem.WriteAllText(backupPath, content);

        var hash = ComputeSha256(content);
        return new ConfigBackupRecord(filePath, backupPath, timestamp, hash);
    }

    public OperationResult RestoreBackup(string backupPath, string targetPath)
    {
        if (!_fileSystem.FileExists(backupPath))
        {
            return OperationResult.Fail($"Backup file not found: {backupPath}", "BACKUP_NOT_FOUND");
        }

        try
        {
            var backupContent = _fileSystem.ReadAllText(backupPath);
            _fileSystem.WriteAllText(targetPath, backupContent);
            return OperationResult.Ok($"Restored configuration from backup '{Path.GetFileName(backupPath)}'.", backupPath);
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Failed to restore configuration backup: {ex.Message}", "BACKUP_RESTORE_FAILED");
        }
    }

    public IReadOnlyList<ConfigBackupRecord> ListBackups(string backupSubfolder)
    {
        var safeFolder = Path.Combine(_backupRoot, SanitizeName(backupSubfolder));
        if (!_fileSystem.DirectoryExists(safeFolder))
        {
            return Array.Empty<ConfigBackupRecord>();
        }

        var records = new List<ConfigBackupRecord>();
        foreach (var file in _fileSystem.EnumerateFiles(safeFolder, "*.bak"))
        {
            var content = _fileSystem.ReadAllText(file);
            records.Add(new ConfigBackupRecord(
                string.Empty,
                file,
                _fileSystem.GetLastWriteTimeUtc(file),
                ComputeSha256(content)));
        }

        return records.OrderByDescending(r => r.CreatedAtUtc).ToList();
    }

    private static string ComputeSha256(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes);
    }

    private static string SanitizeName(string name) =>
        string.Concat(name.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'));
}
