using AV.Orch.Core.Models;

namespace AV.Orch.Core.Abstractions;

public interface IAiProviderAdapter
{
    ProviderId ProviderId { get; }
    string DisplayName { get; }
    string VendorName { get; }
    string ServerEntryName { get; }

    Task<ProviderDetectionResult> DetectInstallationAsync(CancellationToken cancellationToken = default);
    Task<string?> GetInstalledVersionAsync(CancellationToken cancellationToken = default);
    Task<ProviderAuthResult> CheckAuthenticationAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> BeginProviderLoginAsync(CancellationToken cancellationToken = default);
    Task<ProviderRegistrationState> GetRegistrationStateAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> InstallMcpRegistrationAsync(string pyRevitExecutablePath, int? revitYear = null, CancellationToken cancellationToken = default);
    Task<OperationResult> RemoveMcpRegistrationAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> ValidateConnectionAsync(string pyRevitExecutablePath, CancellationToken cancellationToken = default);
    Task<OperationResult> RepairRegistrationAsync(string pyRevitExecutablePath, int? revitYear = null, CancellationToken cancellationToken = default);
}

public interface IProcessRunner
{
    Task<ProcessExecutionResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        TimeSpan? timeout = null,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        CancellationToken cancellationToken = default);

    Task<OperationResult> LaunchInteractiveAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default);

    Task<string?> FindExecutableInPathAsync(string executableName, CancellationToken cancellationToken = default);
}

public interface IFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    string ReadAllText(string path);
    void WriteAllText(string path, string content);
    void CopyFile(string sourcePath, string destinationPath, bool overwrite = true);
    void DeleteFile(string path);
    void CreateDirectory(string path);
    void DeleteDirectory(string path, bool recursive = false);
    IEnumerable<string> EnumerateFiles(string directory, string searchPattern = "*");
    IEnumerable<string> EnumerateDirectories(string directory);
    DateTimeOffset GetLastWriteTimeUtc(string path);
}

public interface IEnvironmentProvider
{
    string UserProfileDirectory { get; }
    string LocalAppDataDirectory { get; }
    string RoamingAppDataDirectory { get; }
    string ProgramFilesDirectory { get; }
    bool IsWindows { get; }
    bool IsElevated { get; }
    string? GetEnvironmentVariable(string name);
}

public interface ISecretRedactor
{
    string Redact(string input);
}

public interface IConfigurationBackupService
{
    ConfigBackupRecord CreateBackup(string filePath, string backupSubfolder);
    OperationResult RestoreBackup(string backupPath, string targetPath);
    IReadOnlyList<ConfigBackupRecord> ListBackups(string backupSubfolder);
}

public interface IAppLogger
{
    void Info(string category, string message);
    void Warn(string category, string message);
    void Error(string category, string message, Exception? exception = null);
    string LogDirectory { get; }
    IReadOnlyList<string> ReadRecentLines(int maxLines = 200);
    void PurgeOldLogs(int retentionDays);
}

public interface IAuditLogService
{
    void Record(string action, string actor, string target, bool succeeded, string summary);
    IReadOnlyList<AuditEntry> GetRecentEntries(int maxCount = 100);
}

public interface ISettingsService
{
    AppSettings Load();
    void Save(AppSettings settings);
    AppSettings RestoreDefaults();
    string SettingsFilePath { get; }
}
