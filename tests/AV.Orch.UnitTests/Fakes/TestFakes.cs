using AV.Orch.Core.Abstractions;
using AV.Orch.Core.Models;

namespace AV.Orch.UnitTests.Fakes;

public sealed class FakeEnvironmentProvider : IEnvironmentProvider
{
    public string UserProfileDirectory { get; set; } = "/home/testuser";
    public string LocalAppDataDirectory { get; set; } = "/home/testuser/AppData/Local";
    public string RoamingAppDataDirectory { get; set; } = "/home/testuser/AppData/Roaming";
    public string ProgramFilesDirectory { get; set; } = "/ProgramFiles";
    public bool IsWindows { get; set; } = true;
    public bool IsElevated { get; set; }
    public Dictionary<string, string> EnvVars { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? GetEnvironmentVariable(string name) =>
        EnvVars.TryGetValue(name, out var val) ? val : null;
}

public sealed class InMemoryFileSystem : IFileSystem
{
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _timestamps = new(StringComparer.OrdinalIgnoreCase);

    public bool FailNextWrite { get; set; }

    public bool FileExists(string path) => _files.ContainsKey(Normalize(path));
    public bool DirectoryExists(string path) => _directories.Contains(Normalize(path));

    public string ReadAllText(string path)
    {
        var key = Normalize(path);
        if (!_files.TryGetValue(key, out var content))
        {
            throw new FileNotFoundException($"File not found: {path}", path);
        }

        return content;
    }

    public void WriteAllText(string path, string content)
    {
        if (FailNextWrite)
        {
            FailNextWrite = false;
            throw new IOException("Simulated disk write failure.");
        }

        var key = Normalize(path);
        EnsureParentDirectories(key);
        _files[key] = content;
        _timestamps[key] = DateTimeOffset.UtcNow;
    }

    public void CopyFile(string sourcePath, string destinationPath, bool overwrite = true)
    {
        var content = ReadAllText(sourcePath);
        WriteAllText(destinationPath, content);
    }

    public void DeleteFile(string path)
    {
        var key = Normalize(path);
        _files.Remove(key);
        _timestamps.Remove(key);
    }

    public void CreateDirectory(string path)
    {
        var key = Normalize(path);
        _directories.Add(key);
        EnsureParentDirectories(key);
    }

    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern = "*")
    {
        var dir = Normalize(directory).TrimEnd('/');
        var prefix = dir + "/";
        var suffix = searchPattern.StartsWith('*') ? searchPattern[1..] : string.Empty;

        return _files.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Where(k => !k[prefix.Length..].Contains('/'))
            .Where(k => string.IsNullOrEmpty(suffix) || k.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public IEnumerable<string> EnumerateDirectories(string directory)
    {
        var dir = Normalize(directory).TrimEnd('/');
        var prefix = dir + "/";
        return _directories
            .Where(d => d.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Where(d => !d[prefix.Length..].Contains('/'))
            .ToList();
    }

    public DateTimeOffset GetLastWriteTimeUtc(string path) =>
        _timestamps.TryGetValue(Normalize(path), out var ts) ? ts : DateTimeOffset.UtcNow;

    public void SetLastWriteTimeUtc(string path, DateTimeOffset timestamp) =>
        _timestamps[Normalize(path)] = timestamp;

    private void EnsureParentDirectories(string normalizedPath)
    {
        var idx = normalizedPath.LastIndexOf('/');
        while (idx > 0)
        {
            var parent = normalizedPath[..idx];
            _directories.Add(parent);
            idx = parent.LastIndexOf('/');
        }
    }

    private static string Normalize(string path) => path.Replace('\\', '/');
}

public sealed class FakeProcessRunner : IProcessRunner
{
    public Dictionary<string, string> PathExecutables { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Func<string, IReadOnlyList<string>, ProcessExecutionResult>? RunHandler { get; set; }
    public Func<string, IReadOnlyList<string>, OperationResult>? InteractiveHandler { get; set; }
    public List<(string FileName, string Args)> Invocations { get; } = new();

    public Task<ProcessExecutionResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        TimeSpan? timeout = null,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        CancellationToken cancellationToken = default)
    {
        Invocations.Add((fileName, string.Join(" ", arguments)));
        if (RunHandler is not null)
        {
            return Task.FromResult(RunHandler(fileName, arguments));
        }

        return Task.FromResult(new ProcessExecutionResult(0, string.Empty, string.Empty));
    }

    public Task<OperationResult> LaunchInteractiveAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        Invocations.Add((fileName, string.Join(" ", arguments)));
        if (InteractiveHandler is not null)
        {
            return Task.FromResult(InteractiveHandler(fileName, arguments));
        }

        return Task.FromResult(OperationResult.Ok($"Launched {fileName}"));
    }

    public Task<string?> FindExecutableInPathAsync(string executableName, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(PathExecutables.TryGetValue(executableName, out var found) ? found : null);
    }
}

public sealed class FakeMcpStdioProbe : IMcpStdioProbe
{
    public OperationResult NextResult { get; set; } = OperationResult.Ok("MCP probe OK");

    public Task<OperationResult> ProbeInitializeAndToolsAsync(
        string command,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(NextResult);
}
