using AV.Orch.Core.Abstractions;
using AV.Orch.Core.Models;

namespace AV.Orch.Providers.Chat;

public sealed record ChatTurnResult(
    bool Succeeded,
    ProviderId Provider,
    string ResponseText,
    string? ErrorMessage = null,
    TimeSpan Elapsed = default);

public sealed class ProviderChatService
{
    private readonly IProcessRunner _processRunner;
    private readonly IAppLogger _logger;

    public ProviderChatService(IProcessRunner processRunner, IAppLogger logger)
    {
        _processRunner = processRunner;
        _logger = logger;
    }

    public static (string Executable, IReadOnlyList<string> Arguments) BuildNonInteractiveCommand(
        ProviderId provider,
        string prompt,
        bool continueConversation = false)
    {
        return provider switch
        {
            ProviderId.ClaudeCode => continueConversation
                ? ("claude", new[] { "-p", "--continue", prompt })
                : ("claude", new[] { "-p", prompt }),
            ProviderId.Codex => ("codex", new[] { "exec", prompt }),
            ProviderId.Gemini => ("gemini", new[] { "-p", prompt }),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unsupported provider.")
        };
    }

    public async Task<ChatTurnResult> SendPromptAsync(
        ProviderId provider,
        string prompt,
        bool continueConversation = false,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return new ChatTurnResult(false, provider, string.Empty, "Prompt cannot be empty.");
        }

        var (exeName, args) = BuildNonInteractiveCommand(provider, prompt.Trim(), continueConversation);
        var exePath = await _processRunner.FindExecutableInPathAsync(exeName, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return new ChatTurnResult(
                false,
                provider,
                string.Empty,
                $"'{exeName}' CLI was not found on PATH. Click 'Install Latest CLI' first.");
        }

        var started = DateTimeOffset.UtcNow;
        var res = await _processRunner.RunAsync(
            exePath,
            args,
            timeout: timeout ?? TimeSpan.FromMinutes(5),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var elapsed = DateTimeOffset.UtcNow - started;

        if (res.TimedOut)
        {
            _logger.Warn("ProviderChat", $"{provider} prompt timed out after {elapsed.TotalSeconds:F1}s.");
            return new ChatTurnResult(
                false,
                provider,
                res.StandardOutput,
                "Request timed out. If Revit is waiting for a Keep/Discard approval prompt, answer it in Revit and retry.",
                elapsed);
        }

        if (!res.Succeeded)
        {
            var err = string.IsNullOrWhiteSpace(res.StandardError) ? res.StandardOutput : res.StandardError;
            _logger.Warn("ProviderChat", $"{provider} exited with {res.ExitCode}: {err}");
            return new ChatTurnResult(false, provider, res.StandardOutput, err, elapsed);
        }

        return new ChatTurnResult(true, provider, res.StandardOutput, null, elapsed);
    }
}
