using PromptPolisher.Core.Security;
using Xunit;

namespace PromptPolisher.Tests;

public sealed class RedactionTests
{
    [Theory]
    [InlineData("sk-1234567890abcdef", "sk-1234567890abcdef")]
    [InlineData("Authorization: Bearer abc.def.123", "abc.def.123")]
    [InlineData("api_key=super-secret-value", "super-secret-value")]
    [InlineData("Cookie: session=abcdef123456", "abcdef123456")]
    [InlineData("password: hunter2", "hunter2")]
    public void Redact_RemovesSecretValues(string input, string secret)
    {
        var result = Redaction.Redact(input);

        Assert.DoesNotContain(secret, result);
    }

    [Fact]
    public void Redact_KeepsNormalText()
    {
        const string input = "这是一段普通的错误说明，不包含密钥。";
        Assert.Equal(input, Redaction.Redact(input));
    }

    [Fact]
    public void Preview_TruncatesAndRedacts()
    {
        var preview = Redaction.Preview("sk-1234567890abcdef and more text", 10);

        Assert.DoesNotContain("sk-1234567890abcdef", preview);
        Assert.True(preview.Length <= 11);
    }
}
