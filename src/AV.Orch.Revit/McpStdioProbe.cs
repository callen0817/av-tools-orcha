using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AV.Orch.Core.Abstractions;
using AV.Orch.Core.Models;
using AV.Orch.Security;

namespace AV.Orch.Revit;

public sealed class McpStdioProbe : IMcpStdioProbe
{
    public async Task<OperationResult> ProbeInitializeAndToolsAsync(
        string command,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var pathCheck = PathValidator.ValidateLocalExecutablePath(command);
        if (!pathCheck.Succeeded)
        {
            return pathCheck;
        }

        var argsCheck = PathValidator.ValidateMcpArguments(arguments);
        if (!argsCheck.Succeeded)
        {
            return argsCheck;
        }

        var psi = new ProcessStartInfo
        {
            FileName = command,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };
        try
        {
            if (!process.Start())
            {
                return OperationResult.Fail("Failed to launch MCP server process.", "MCP_START_FAILED");
            }
        }
        catch (Win32Exception ex)
        {
            return OperationResult.Fail($"MCP server executable could not be started: {ex.Message}", "MCP_START_FAILED");
        }
        catch (FileNotFoundException ex)
        {
            return OperationResult.Fail($"MCP server executable not found: {ex.Message}", "MCP_NOT_FOUND");
        }

        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(10);
        using var timeoutCts = new CancellationTokenSource(effectiveTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            // 1. Send JSON-RPC 2.0 initialize
            var initRequest = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new { },
                    clientInfo = new { name = "av-orch-probe", version = "1.0.0" }
                }
            });

            await process.StandardInput.WriteLineAsync(initRequest.AsMemory(), linkedCts.Token).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(linkedCts.Token).ConfigureAwait(false);

            var initResponseLine = await ReadNextJsonLineAsync(process.StandardOutput, linkedCts.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(initResponseLine))
            {
                var err = await process.StandardError.ReadToEndAsync(linkedCts.Token).ConfigureAwait(false);
                return OperationResult.Fail(
                    $"MCP server closed without responding to initialize. {err}".Trim(),
                    "MCP_NO_INIT_RESPONSE");
            }

            if (!ValidateJsonRpcSuccess(initResponseLine, expectedId: 1, out var initError))
            {
                return OperationResult.Fail($"MCP initialize failed: {initError}", "MCP_INIT_ERROR");
            }

            // 2. Send notifications/initialized
            var initializedNotif = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                method = "notifications/initialized"
            });
            await process.StandardInput.WriteLineAsync(initializedNotif.AsMemory(), linkedCts.Token).ConfigureAwait(false);

            // 3. Send tools/list
            var listToolsReq = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "tools/list",
                @params = new { }
            });
            await process.StandardInput.WriteLineAsync(listToolsReq.AsMemory(), linkedCts.Token).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(linkedCts.Token).ConfigureAwait(false);

            var toolsResponseLine = await ReadNextJsonLineAsync(process.StandardOutput, linkedCts.Token).ConfigureAwait(false);
            string? toolsError = null;
            if (string.IsNullOrWhiteSpace(toolsResponseLine) || !ValidateJsonRpcSuccess(toolsResponseLine, expectedId: 2, out toolsError))
            {
                return OperationResult.Fail($"MCP tools/list failed: {toolsError ?? "Empty response"}", "MCP_TOOLS_ERROR");
            }

            return OperationResult.Ok("MCP stdio server initialized and responded to tools/list.");
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return OperationResult.Fail("MCP stdio probe timed out waiting for JSON-RPC response.", "MCP_PROBE_TIMEOUT");
        }
        finally
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
                // Best effort cleanup
            }
        }
    }

    private static async Task<string?> ReadNextJsonLineAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                return null;
            }

            var trimmed = line.Trim();
            if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
            {
                return trimmed;
            }
        }

        return null;
    }

    private static bool ValidateJsonRpcSuccess(string line, int expectedId, out string? error)
    {
        error = null;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var errEl))
            {
                error = errEl.ToString();
                return false;
            }

            if (root.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var id) && id != expectedId)
            {
                error = $"Unexpected JSON-RPC id {id}, expected {expectedId}.";
                return false;
            }

            if (!root.TryGetProperty("result", out _))
            {
                error = "JSON-RPC response missing 'result' object.";
                return false;
            }

            return true;
        }
        catch (JsonException ex)
        {
            error = $"Invalid JSON-RPC payload: {ex.Message}";
            return false;
        }
    }
}
