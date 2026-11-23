using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using AV.Orch.Core.Abstractions;
using AV.Orch.Core.Models;

namespace AV.Orch.Core.Services;

public sealed class SystemProcessRunner : IProcessRunner
{
    public async Task<ProcessExecutionResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        TimeSpan? timeout = null,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return new ProcessExecutionResult(-1, string.Empty, "Executable path cannot be empty.", false, "Empty executable path.");
        }

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            psi.WorkingDirectory = workingDirectory;
        }

        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        if (environmentVariables is not null)
        {
            foreach (var kv in environmentVariables)
            {
                psi.Environment[kv.Key] = kv.Value;
            }
        }

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdout.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderr.AppendLine(e.Data);
            }
        };

        try
        {
            if (!process.Start())
            {
                return new ProcessExecutionResult(-1, string.Empty, "Failed to start process.", false, $"Could not start '{fileName}'.");
            }
        }
        catch (Win32Exception ex)
        {
            return new ProcessExecutionResult(-1, string.Empty, ex.Message, false, ex.Message);
        }
        catch (FileNotFoundException ex)
        {
            return new ProcessExecutionResult(-1, string.Empty, ex.Message, false, ex.Message);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
        using var timeoutCts = new CancellationTokenSource(effectiveTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
            return new ProcessExecutionResult(process.ExitCode, stdout.ToString().Trim(), stderr.ToString().Trim());
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return new ProcessExecutionResult(-1, stdout.ToString().Trim(), stderr.ToString().Trim(), TimedOut: true);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
    }

    public Task<OperationResult> LaunchInteractiveAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = true
            };

            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                psi.WorkingDirectory = workingDirectory;
            }

            if (arguments.Count > 0)
            {
                psi.Arguments = string.Join(" ", arguments.Select(QuoteArgument));
            }

            Process.Start(psi);
            return Task.FromResult(OperationResult.Ok($"Launched interactive process '{fileName}'."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult.Fail(
                $"Failed to launch '{fileName}': {ex.Message}",
                "LAUNCH_FAILED",
                $"Open a terminal and run '{fileName} {string.Join(" ", arguments)}' manually."));
        }
    }

    public async Task<string?> FindExecutableInPathAsync(string executableName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(executableName))
        {
            return null;
        }

        var locator = OperatingSystem.IsWindows() ? "where.exe" : "which";
        var result = await RunAsync(locator, new[] { executableName }, timeout: TimeSpan.FromSeconds(5), cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return null;
        }

        var firstLine = result.StandardOutput
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        return string.IsNullOrWhiteSpace(firstLine) ? null : firstLine.Trim();
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    private static string QuoteArgument(string arg) =>
        arg.Contains(' ') || arg.Contains('"') ? $"\"{arg.Replace("\"", "\\\"")}\"" : arg;
}
