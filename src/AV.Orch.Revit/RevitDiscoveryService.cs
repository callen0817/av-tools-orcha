using System.Text.RegularExpressions;
using AV.Orch.Core.Abstractions;
using AV.Orch.Core.Models;

namespace AV.Orch.Revit;

public sealed class RevitDiscoveryService : IRevitDiscoveryService
{
    public const int MinSupportedRevitYear = 2023;

    private static readonly Regex RevitFolderRegex = new(@"^Revit\s+(?<year>20\d{2})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IFileSystem _fileSystem;
    private readonly IEnvironmentProvider _environment;

    public RevitDiscoveryService(IFileSystem fileSystem, IEnvironmentProvider environment)
    {
        _fileSystem = fileSystem;
        _environment = environment;
    }

    public Task<IReadOnlyList<RevitInstallation>> DiscoverInstallationsAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<RevitInstallation>();
        var autodeskRoot = Path.Combine(_environment.ProgramFilesDirectory, "Autodesk");

        if (_fileSystem.DirectoryExists(autodeskRoot))
        {
            foreach (var dir in _fileSystem.EnumerateDirectories(autodeskRoot))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var folderName = Path.GetFileName(dir);
                var match = RevitFolderRegex.Match(folderName);
                if (!match.Success || !int.TryParse(match.Groups["year"].Value, out var year))
                {
                    continue;
                }

                var exePath = Path.Combine(dir, "Revit.exe");
                if (!_fileSystem.FileExists(exePath))
                {
                    continue;
                }

                var isSupported = year >= MinSupportedRevitYear;
                results.Add(new RevitInstallation(
                    year,
                    $"Autodesk Revit {year}",
                    dir,
                    exePath,
                    isSupported));
            }
        }

        return Task.FromResult<IReadOnlyList<RevitInstallation>>(
            results.OrderByDescending(r => r.Year).ToList());
    }
}
