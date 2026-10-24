using System.Text.RegularExpressions;

namespace PromptPolisher.Core.Security;

/// <summary>
/// 日志脱敏。原则：绝不记录完整 Prompt；密钥/Token/Cookie 一律替换。
/// </summary>
public static class Redaction
{
    private static readonly Regex[] Patterns =
    {
        new(@"(?i)(sk-[A-Za-z0-9_\-]{8,})", RegexOptions.Compiled),
        new(@"(?i)(Bearer\s+)[A-Za-z0-9\._\-]+", RegexOptions.Compiled),
        new(@"(?i)((?:api[_-]?key|access[_-]?token|refresh[_-]?token|token|cookie|authorization|password)\s*[:=]\s*)[^\s,;]+", RegexOptions.Compiled),
        new(@"(?i)(Cookie:\s*)[^\r\n]+", RegexOptions.Compiled)
    };

    public static string Redact(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var result = input;
        result = Patterns[0].Replace(result, "[REDACTED_API_KEY]");
        result = Patterns[1].Replace(result, "$1[REDACTED_TOKEN]");
        result = Patterns[2].Replace(result, "$1[REDACTED]");
        result = Patterns[3].Replace(result, "$1[REDACTED_COOKIE]");
        return result;
    }

    /// <summary>用于日志的短摘要：只保留长度与前 20 个字符（脱敏后）。</summary>
    public static string Preview(string? input, int max = 20)
    {
        if (string.IsNullOrEmpty(input))
        {
            return "<empty>";
        }

        var redacted = Redact(input);
        return redacted.Length <= max ? redacted : redacted[..max] + "…";
    }
}
