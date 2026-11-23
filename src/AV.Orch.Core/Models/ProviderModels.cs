namespace AV.Orch.Core.Models;

public sealed record OperationResult(
    bool Succeeded,
    string Message,
    string? ErrorCode = null,
    string? RecoveryAction = null,
    string? BackupPath = null)
{
    public static OperationResult Ok(string message, string? backupPath = null) =>
        new(true, message, null, null, backupPath);

    public static OperationResult Fail(string message, string errorCode, string? recoveryAction = null, string? backupPath = null) =>
        new(false, message, errorCode, recoveryAction, backupPath);
}

public sealed record ProcessExecutionResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut = false,
    string? ExecutableNotFoundError = null)
{
    public bool Succeeded => ExitCode == 0 && !TimedOut && ExecutableNotFoundError is null;
}

public sealed record ProviderDetectionResult(
    ProviderId Provider,
    InstallationStatus Status,
    string? ExecutablePath,
    string? Version,
    string Message,
    bool IsSupportedVersion = true);

public sealed record ProviderAuthResult(
    ProviderId Provider,
    AuthenticationStatus Status,
    string Message,
    string? LoginCommand = null,
    string? AccountHint = null);

public sealed record ProviderRegistrationState(
    ProviderId Provider,
    RegistrationStatus Status,
    string? ConfigFilePath,
    string? RegisteredCommand,
    IReadOnlyList<string> RegisteredArgs,
    bool OwnedByAvOrch,
    string Message,
    int OtherServerCount = 0);

public sealed record ProviderSnapshot(
    ProviderId Provider,
    string DisplayName,
    string VendorName,
    ProviderDetectionResult Detection,
    ProviderAuthResult Authentication,
    ProviderRegistrationState Registration,
    DateTimeOffset RefreshedAtUtc);

public sealed record ConfigBackupRecord(
    string OriginalPath,
    string BackupPath,
    DateTimeOffset CreatedAtUtc,
    string Sha256Hash);
