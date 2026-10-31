using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.Services;
using ReviewApp.Api.Tests.Support;

namespace ReviewApp.Api.Tests.Unit;

public class TokenServiceTests
{
    private readonly DefaultHttpContext _httpContext = new();
    private readonly TokenService _service;

    private static readonly User User = new() { ID = 42, TokenVersion = 3, Username = "alice", Email = "alice@test.local" };

    public TokenServiceTests()
    {
        _service = new TokenService(TestTokens.Configuration, new HttpContextAccessor { HttpContext = _httpContext });
    }

    private string SetCookieHeader() => _httpContext.Response.Headers.SetCookie.ToString();

    private string IssuedToken()
    {
        var header = SetCookieHeader();
        var start = header.IndexOf('=') + 1;
        return header[start..header.IndexOf(';')];
    }

    [Fact]
    public void IssueAuthCookie_SetsHostPrefixedHttpOnlySecureStrictCookie()
    {
        _service.IssueAuthCookie(User);

        var header = SetCookieHeader().ToLowerInvariant();
        header.ShouldStartWith($"{TokenService.CookieName.ToLowerInvariant()}=");
        header.ShouldContain("httponly");
        header.ShouldContain("secure");
        header.ShouldContain("samesite=strict");
        header.ShouldContain("path=/");
        header.ShouldNotContain("domain=");
    }

    [Fact]
    public void IssueAuthCookie_TokenCarriesOnlyIdAndVersion()
    {
        _service.IssueAuthCookie(User);

        var claims = TestTokens.ReadClaims(IssuedToken());
        claims["id"].ShouldBe("42");
        claims["ver"].ShouldBe("3");
        claims["iss"].ShouldBe(ApiFactory.JwtIssuer);
        claims["aud"].ShouldBe(ApiFactory.JwtAudience);
        // Nothing personal in a payload that anyone holding the token can read
        claims.Keys.ShouldNotContain(ClaimTypes.Name);
        claims.Values.ShouldNotContain("alice");
        claims.Values.ShouldNotContain("alice@test.local");
    }

    [Fact]
    public void IssueAuthCookie_ExpiresAfterEightHours()
    {
        _service.IssueAuthCookie(User);

        var expires = DateTimeOffset.FromUnixTimeSeconds(long.Parse(TestTokens.ReadClaims(IssuedToken())["exp"])).UtcDateTime;
        expires.ShouldBe(DateTime.UtcNow.AddHours(8), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void DeleteAuthCookie_ExpiresTheCookieWithTheSameAttributes()
    {
        _service.DeleteAuthCookie();

        var header = SetCookieHeader().ToLowerInvariant();
        header.ShouldStartWith($"{TokenService.CookieName.ToLowerInvariant()}=;");
        header.ShouldContain("expires=thu, 01 jan 1970");
        header.ShouldContain("secure");
        header.ShouldContain("path=/");
    }

    [Fact]
    public void IssueAuthCookie_WithoutHttpContext_Throws()
    {
        // HttpContextAccessor stores the context in a static AsyncLocal, so the constructor's context must be cleared explicitly
        var service = new TokenService(TestTokens.Configuration, new HttpContextAccessor { HttpContext = null });

        Should.Throw<InvalidOperationException>(() => service.IssueAuthCookie(User));
    }

    [Fact]
    public void TwoFactorChallenge_RoundTripsUserAndVersion()
    {
        var challenge = _service.CreateTwoFactorChallenge(User);

        _service.ValidateTwoFactorChallenge(challenge).ShouldBe((42, 3));
    }

    [Fact]
    public void TwoFactorChallenge_HasItsOwnAudienceAndTenMinuteLifetime()
    {
        var claims = TestTokens.ReadClaims(_service.CreateTwoFactorChallenge(User));

        claims["aud"].ShouldBe($"{ApiFactory.JwtAudience}:2fa-challenge");
        claims["purpose"].ShouldBe("2fa-login");
        DateTimeOffset.FromUnixTimeSeconds(long.Parse(claims["exp"])).UtcDateTime
            .ShouldBe(DateTime.UtcNow.AddMinutes(10), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void ValidateTwoFactorChallenge_RejectsASessionToken()
    {
        _service.IssueAuthCookie(User);

        _service.ValidateTwoFactorChallenge(IssuedToken()).ShouldBeNull();
    }

    [Fact]
    public void ValidateTwoFactorChallenge_RejectsExpiredChallenge()
    {
        var expired = TestTokens.Create(
            [new Claim("id", "42"), new Claim("ver", "3"), new Claim("purpose", "2fa-login")],
            audience: $"{ApiFactory.JwtAudience}:2fa-challenge",
            expires: DateTime.UtcNow.AddMinutes(-1)); // beyond the 30 s clock skew

        _service.ValidateTwoFactorChallenge(expired).ShouldBeNull();
    }

    [Fact]
    public void ValidateTwoFactorChallenge_RejectsWrongPurposeOrMissingClaims()
    {
        var audience = $"{ApiFactory.JwtAudience}:2fa-challenge";

        _service.ValidateTwoFactorChallenge(TestTokens.Create(
            [new Claim("id", "42"), new Claim("ver", "3"), new Claim("purpose", "something-else")], audience)).ShouldBeNull();
        _service.ValidateTwoFactorChallenge(TestTokens.Create(
            [new Claim("ver", "3"), new Claim("purpose", "2fa-login")], audience)).ShouldBeNull();
        _service.ValidateTwoFactorChallenge(TestTokens.Create(
            [new Claim("id", "42"), new Claim("ver", "x"), new Claim("purpose", "2fa-login")], audience)).ShouldBeNull();
    }

    [Fact]
    public void ValidateTwoFactorChallenge_RejectsForeignKeyTamperingAndGarbage()
    {
        var claims = new[] { new Claim("id", "42"), new Claim("ver", "3"), new Claim("purpose", "2fa-login") };
        var foreignKey = TestTokens.Create(claims, $"{ApiFactory.JwtAudience}:2fa-challenge", key: "another-signing-key-that-is-long-enough!!");
        var genuine = _service.CreateTwoFactorChallenge(User);
        var tampered = genuine[..^4] + (genuine[^4..] == "AAAA" ? "BBBB" : "AAAA");

        _service.ValidateTwoFactorChallenge(foreignKey).ShouldBeNull();
        _service.ValidateTwoFactorChallenge(tampered).ShouldBeNull();
        _service.ValidateTwoFactorChallenge("not-a-jwt").ShouldBeNull();
        _service.ValidateTwoFactorChallenge(string.Empty).ShouldBeNull();
    }
}
