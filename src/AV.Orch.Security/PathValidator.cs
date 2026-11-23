using AV.Orch.Core.Models;

namespace AV.Orch.Security;

public static class PathValidator
{
    private static readonly char[] ForbiddenShellChars = { '&', '|', ';', '<', '>', '`', '\r', '\n', '\0' };

    public static OperationResult ValidateLocalExecutablePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return OperationResult.Fail("Executable path is empty.", "INVALID_PATH");
        }

        var trimmed = path.Trim();
        if (trimmed.IndexOfAny(ForbiddenShellChars) >= 0)
        {
            return OperationResult.Fail(
                "Executable path contains disallowed control or shell characters.",
                "UNSAFE_PATH_CHARS");
        }

        // Reject UNC / network paths (\\server\share or //server/share)
        if (trimmed.StartsWith(@"\\", StringComparison.Ordinal) || trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            return OperationResult.Fail(
                "Network (UNC) executable paths are not permitted for local MCP registration.",
                "UNC_PATH_BLOCKED",
                "Install the executable on a local drive.");
        }

        // Allow absolute Windows drive paths (e.g. C:\...) or Unix rooted paths (/...)
        var isWindowsDrivePath = trimmed.Length >= 3 &&
                                 char.IsLetter(trimmed[0]) &&
                                 trimmed[1] == ':' &&
                                 (trimmed[2] == '\\' || trimmed[2] == '/');

        if (!isWindowsDrivePath && !Path.IsPathRooted(trimmed))
        {
            return OperationResult.Fail(
                "Executable path must be an absolute local path.",
                "RELATIVE_PATH_BLOCKED");
        }

        return OperationResult.Ok("Executable path validated.");
    }

    public static OperationResult ValidateMcpArguments(IReadOnlyList<string> arguments)
    {
        foreach (var arg in arguments)
        {
            if (arg is null || arg.IndexOfAny(ForbiddenShellChars) >= 0)
            {
                return OperationResult.Fail(
                    "MCP argument contains disallowed control or shell characters.",
                    "UNSAFE_ARGUMENT");
            }
        }

        return OperationResult.Ok("MCP arguments validated.");
    }
}
