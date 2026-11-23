using System.Security.Principal;
using AV.Orch.Core.Abstractions;

namespace AV.Orch.Core.Services;

public sealed class LocalFileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public string ReadAllText(string path) => File.ReadAllText(path);

    public void WriteAllText(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Atomic write via temporary file in the same directory
        var tempPath = $"{path}.avorch.tmp.{Guid.NewGuid():N}";
        File.WriteAllText(tempPath, content);
        File.Move(tempPath, path, overwrite: true);
    }

    public void CopyFile(string sourcePath, string destinationPath, bool overwrite = true)
    {
        var dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.Copy(sourcePath, destinationPath, overwrite);
    }

    public void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern = "*") =>
        Directory.Exists(directory) ? Directory.EnumerateFiles(directory, searchPattern) : Array.Empty<string>();

    public IEnumerable<string> EnumerateDirectories(string directory) =>
        Directory.Exists(directory) ? Directory.EnumerateDirectories(directory) : Array.Empty<string>();

    public DateTimeOffset GetLastWriteTimeUtc(string path) => File.GetLastWriteTimeUtc(path);
}

public sealed class SystemEnvironmentProvider : IEnvironmentProvider
{
    public string UserProfileDirectory => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public string LocalAppDataDirectory => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public string RoamingAppDataDirectory => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    public string ProgramFilesDirectory => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    public bool IsWindows => OperatingSystem.IsWindows();

    public bool IsElevated
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    using var identity = WindowsIdentity.GetCurrent();
                    var principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }
    }

    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);
}
