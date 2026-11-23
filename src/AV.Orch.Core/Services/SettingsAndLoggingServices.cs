using System.Text.Json;
using AV.Orch.Core.Abstractions;
using AV.Orch.Core.Models;

namespace AV.Orch.Core.Services;

public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly IFileSystem _fileSystem;

    public string SettingsFilePath { get; }

    public JsonSettingsService(IFileSystem fileSystem, IEnvironmentProvider environment)
    {
        _fileSystem = fileSystem;
        SettingsFilePath = Path.Combine(environment.LocalAppDataDirectory, "ArtificialVision", "AVOrch", "settings.json");
    }

    public AppSettings Load()
    {
        if (!_fileSystem.FileExists(SettingsFilePath))
        {
            return new AppSettings();
        }

        try
        {
            var json = _fileSystem.ReadAllText(SettingsFilePath);
            return JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var safeSettings = settings with { TelemetryEnabled = false };
        var json = JsonSerializer.Serialize(safeSettings, SerializerOptions);
        _fileSystem.WriteAllText(SettingsFilePath, json);
    }

    public AppSettings RestoreDefaults()
    {
        var defaults = new AppSettings();
        Save(defaults);
        return defaults;
    }
}

public sealed class StructuredAppLogger : IAppLogger
{
    private readonly IFileSystem _fileSystem;
    private readonly ISecretRedactor _redactor;
    private readonly object _syncLock = new();

    public string LogDirectory { get; }

    public StructuredAppLogger(IFileSystem fileSystem, IEnvironmentProvider environment, ISecretRedactor redactor)
    {
        _fileSystem = fileSystem;
        _redactor = redactor;
        LogDirectory = Path.Combine(environment.LocalAppDataDirectory, "ArtificialVision", "AVOrch", "logs");
        _fileSystem.CreateDirectory(LogDirectory);
    }

    public void Info(string category, string message) => Write("INFO", category, message);
    public void Warn(string category, string message) => Write("WARN", category, message);

    public void Error(string category, string message, Exception? exception = null)
    {
        var full = exception is null ? message : $"{message} | {exception.GetType().Name}: {exception.Message}";
        Write("ERROR", category, full);
    }

    public IReadOnlyList<string> ReadRecentLines(int maxLines = 200)
    {
        lock (_syncLock)
        {
            var logFile = GetTodayLogFile();
            if (!_fileSystem.FileExists(logFile))
            {
                return Array.Empty<string>();
            }

            var lines = _fileSystem.ReadAllText(logFile)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return lines.TakeLast(Math.Max(1, maxLines)).ToList();
        }
    }

    public void PurgeOldLogs(int retentionDays)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, retentionDays));
        foreach (var file in _fileSystem.EnumerateFiles(LogDirectory, "avorch-*.log"))
        {
            if (_fileSystem.GetLastWriteTimeUtc(file) < cutoff)
            {
                _fileSystem.DeleteFile(file);
            }
        }
    }

    private void Write(string level, string category, string message)
    {
        var safeMessage = _redactor.Redact(message);
        var entry = JsonSerializer.Serialize(new
        {
            ts = DateTimeOffset.UtcNow.ToString("O"),
            level,
            category,
            message = safeMessage
        });

        lock (_syncLock)
        {
            var logFile = GetTodayLogFile();
            var existing = _fileSystem.FileExists(logFile) ? _fileSystem.ReadAllText(logFile) : string.Empty;
            _fileSystem.WriteAllText(logFile, existing + entry + Environment.NewLine);
        }
    }

    private string GetTodayLogFile() =>
        Path.Combine(LogDirectory, $"avorch-{DateTime.UtcNow:yyyyMMdd}.log");
}

public sealed class AuditLogService : IAuditLogService
{
    private readonly IFileSystem _fileSystem;
    private readonly ISecretRedactor _redactor;
    private readonly string _auditFilePath;
    private readonly object _syncLock = new();

    public AuditLogService(IFileSystem fileSystem, IEnvironmentProvider environment, ISecretRedactor redactor)
    {
        _fileSystem = fileSystem;
        _redactor = redactor;
        _auditFilePath = Path.Combine(environment.LocalAppDataDirectory, "ArtificialVision", "AVOrch", "audit", "audit.jsonl");
    }

    public void Record(string action, string actor, string target, bool succeeded, string summary)
    {
        var entry = new AuditEntry(
            DateTimeOffset.UtcNow,
            action,
            actor,
            target,
            succeeded,
            _redactor.Redact(summary));

        var line = JsonSerializer.Serialize(entry);
        lock (_syncLock)
        {
            var existing = _fileSystem.FileExists(_auditFilePath) ? _fileSystem.ReadAllText(_auditFilePath) : string.Empty;
            _fileSystem.WriteAllText(_auditFilePath, existing + line + Environment.NewLine);
        }
    }

    public IReadOnlyList<AuditEntry> GetRecentEntries(int maxCount = 100)
    {
        lock (_syncLock)
        {
            if (!_fileSystem.FileExists(_auditFilePath))
            {
                return Array.Empty<AuditEntry>();
            }

            var lines = _fileSystem.ReadAllText(_auditFilePath)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            var entries = new List<AuditEntry>();
            foreach (var line in lines.TakeLast(Math.Max(1, maxCount)))
            {
                try
                {
                    var item = JsonSerializer.Deserialize<AuditEntry>(line);
                    if (item is not null)
                    {
                        entries.Add(item);
                    }
                }
                catch
                {
                    // Skip malformed audit line
                }
            }

            entries.Reverse();
            return entries;
        }
    }
}
