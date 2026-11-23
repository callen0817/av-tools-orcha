namespace AV.Orch.Core.Models;

public enum ProviderId
{
    Codex,
    Gemini,
    ClaudeCode
}

public enum InstallationStatus
{
    Unknown,
    NotInstalled,
    Installed,
    UnsupportedVersion,
    InvalidPath
}

public enum AuthenticationStatus
{
    Unknown,
    NotAuthenticated,
    Authenticated,
    Expired,
    CliUnavailable,
    UnsupportedMode
}

public enum RegistrationStatus
{
    Unknown,
    NotRegistered,
    Registered,
    Conflicting,
    CorruptConfig,
    Error
}

public enum PermissionPolicy
{
    ReadOnly,
    AskBeforeModifications
}

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public enum DiagnosticCategory
{
    Prerequisite,
    ExecutablePath,
    VersionCompatibility,
    McpRegistration,
    Authentication,
    RevitSession,
    RevitDocument,
    PyRevitRuntime,
    ConnectionTimeout,
    ConflictingRegistration
}

public enum CheckStatus
{
    Pass,
    Fail,
    Blocked,
    NotTested
}
