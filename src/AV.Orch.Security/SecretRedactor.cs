using System.Text.RegularExpressions;
using AV.Orch.Core.Abstractions;

namespace AV.Orch.Security;

public sealed class SecretRedactor : ISecretRedactor
{
    private static readonly Regex[] SecretPatterns =
    {
        // OpenAI / Anthropic / generic sk- keys
        new(@"\bsk-(?:proj-|ant-[a-z0-9-]+-)?[A-Za-z0-9_\-]{12,}\b", RegexOptions.Compiled),
        // Google API keys
        new(@"\bAIza[0-9A-Za-z_\-]{20,}\b", RegexOptions.Compiled),
        // Bearer tokens
        new(@"(?i)(Bearer\s+)[A-Za-z0-9\-._~+/]+=*", RegexOptions.Compiled),
        // Key/token/secret/password assignments
        new(@"(?i)((?:api[_-]?key|access[_-]?token|refresh[_-]?token|auth[_-]?token|client[_-]?secret|password|passwd|cookie|authorization)\s*[:=]\s*[""']?)([^\s""',;}{]+)", RegexOptions.Compiled),
        // Known provider env vars
        new(@"(?i)((?:OPENAI_API_KEY|ANTHROPIC_API_KEY|GEMINI_API_KEY|GOOGLE_API_KEY|CLAUDE_API_KEY)\s*=\s*)([^\s""']+)", RegexOptions.Compiled)
    };

    public string Redact(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var result = input;
        result = SecretPatterns[0].Replace(result, "[REDACTED_KEY]");
        result = SecretPatterns[1].Replace(result, "[REDACTED_GOOGLE_KEY]");
        result = SecretPatterns[2].Replace(result, "$1[REDACTED_TOKEN]");
        result = SecretPatterns[3].Replace(result, "$1[REDACTED]");
        result = SecretPatterns[4].Replace(result, "$1[REDACTED]");
        return result;
    }
}
