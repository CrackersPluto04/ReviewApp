using ReviewApp.Api.Services;

namespace ReviewApp.Api.Tests.Unit;

public class SecurityTokensTests
{
    [Fact]
    public void GenerateLinkToken_IsUrlSafeAnd256Bits()
    {
        var token = SecurityTokens.GenerateLinkToken();

        // 32 bytes in base64 without padding
        token.Length.ShouldBe(43);
        token.ShouldMatch("^[A-Za-z0-9_-]+$");
    }

    [Fact]
    public void GenerateLinkToken_IsDifferentEveryTime()
    {
        var tokens = Enumerable.Range(0, 100).Select(_ => SecurityTokens.GenerateLinkToken()).ToHashSet();

        tokens.Count.ShouldBe(100);
    }

    [Fact]
    public void GenerateCode_IsSixDigitsIncludingLeadingZeros()
    {
        var codes = Enumerable.Range(0, 2000).Select(_ => SecurityTokens.GenerateCode()).ToList();

        codes.ShouldAllBe(c => c.Length == SecurityTokens.CodeLength && c.All(char.IsAsciiDigit));
        // With 2000 draws a code starting with 0 is practically certain, it proves the zero padding
        codes.ShouldContain(c => c.StartsWith('0'));
    }

    [Fact]
    public void Hash_IsUppercaseHexSha256AndDeterministic()
    {
        var hash = SecurityTokens.Hash("123456");

        hash.ShouldBe("8D969EEF6ECAD3C29A3A629280E686CF0C3F5D5A86AFF3CA12020C923ADC6C92");
        SecurityTokens.Hash("123456").ShouldBe(hash);
        SecurityTokens.Hash("123457").ShouldNotBe(hash);
    }

    [Fact]
    public void Matches_ComparesAgainstTheStoredHash()
    {
        var stored = SecurityTokens.Hash("secret-value");

        SecurityTokens.Matches("secret-value", stored).ShouldBeTrue();
        SecurityTokens.Matches("Secret-value", stored).ShouldBeFalse();
        SecurityTokens.Matches("secret-value", "short").ShouldBeFalse();
        SecurityTokens.Matches("secret-value", string.Empty).ShouldBeFalse();
    }

    [Theory]
    [InlineData(24 * 60, "24 hours")]
    [InlineData(60, "1 hour")]
    [InlineData(10, "10 minutes")]
    public void Describe_FormatsTheLifetimesUsedInEmails(int minutes, string expected)
    {
        SecurityTokens.Describe(TimeSpan.FromMinutes(minutes)).ShouldBe(expected);
    }

    [Fact]
    public void Lifetimes_MatchTheDocumentedRules()
    {
        SecurityTokens.EmailVerificationLifetime.ShouldBe(TimeSpan.FromHours(24));
        SecurityTokens.EmailChangeLifetime.ShouldBe(TimeSpan.FromHours(1));
        SecurityTokens.TwoFactorCodeLifetime.ShouldBe(TimeSpan.FromMinutes(10));
        SecurityTokens.ResendCooldown.ShouldBe(TimeSpan.FromSeconds(60));
        SecurityTokens.MaxCodeAttempts.ShouldBe(5);
    }
}
