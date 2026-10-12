using Microsoft.IdentityModel.Tokens;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.Services.Interfaces;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace ReviewApp.Api.Services;

public class TokenService : ITokenService
{
    // The __Host- prefix makes the browser enforce Secure, Path=/ and no Domain,
    // so the cookie can't be set or overwritten by a subdomain or over plain http
    public const string CookieName = "__Host-jwt_token";

    // HS256 needs a key of at least 256 bits
    public const int MinKeyBytes = 32;

    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(8);

    // Same lifetime as the emailed code it belongs to
    private static readonly TimeSpan ChallengeLifetime = SecurityTokens.TwoFactorCodeLifetime;
    private const string ChallengeAudienceSuffix = ":2fa-challenge";
    private const string ChallengePurpose = "2fa-login";

    private readonly IConfiguration _configuration;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public TokenService(IConfiguration configuration, IHttpContextAccessor httpContextAccessor)
    {
        _configuration = configuration;
        _httpContextAccessor = httpContextAccessor;
    }

    public void IssueAuthCookie(User user)
    {
        var expires = DateTime.UtcNow.Add(TokenLifetime);
        GetResponse().Cookies.Append(CookieName, CreateToken(user, expires), CreateCookieOptions(expires));
    }

    public void DeleteAuthCookie()
    {
        // Must be deleted with the same attributes it was set with, otherwise some browsers keep the original
        GetResponse().Cookies.Delete(CookieName, CreateCookieOptions(expires: null));
    }

    public string CreateTwoFactorChallenge(User user)
    {
        var claims = new[]
        {
            new Claim("id", user.ID.ToString()),
            // Ties the challenge to the current sessions: logout-all or a password change invalidates it too
            new Claim("ver", user.TokenVersion.ToString()),
            new Claim("purpose", ChallengePurpose)
        };

        return WriteToken(claims, ChallengeAudience, DateTime.UtcNow.Add(ChallengeLifetime));
    }

    public (int UserId, int TokenVersion)? ValidateTwoFactorChallenge(string challenge)
    {
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = _configuration["Jwt:Issuer"],
            ValidAudience = ChallengeAudience,
            IssuerSigningKey = SigningKey,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        try
        {
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var principal = handler.ValidateToken(challenge, parameters, out _);

            if (principal.FindFirst("purpose")?.Value != ChallengePurpose ||
                !int.TryParse(principal.FindFirst("id")?.Value, out var userId) ||
                !int.TryParse(principal.FindFirst("ver")?.Value, out var version))
                return null;

            return (userId, version);
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        {
            return null;
        }
    }

    private string ChallengeAudience => _configuration["Jwt:Audience"] + ChallengeAudienceSuffix;

    private SymmetricSecurityKey SigningKey => new(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!));

    private HttpResponse GetResponse() =>
        _httpContextAccessor.HttpContext?.Response
            ?? throw new InvalidOperationException("No active HTTP response to write the auth cookie to.");

    private static CookieOptions CreateCookieOptions(DateTime? expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        Expires = expires
    };

    private string CreateToken(User user, DateTime expires)
    {
        // Only what the API needs: the user ID and the version used for revocation.
        // Username / email are left out, they go stale after a change and the payload is readable by anyone holding the token.
        var claims = new[]
        {
            new Claim("id", user.ID.ToString()),
            new Claim("ver", user.TokenVersion.ToString())
        };

        return WriteToken(claims, _configuration["Jwt:Audience"], expires);
    }

    private string WriteToken(IEnumerable<Claim> claims, string? audience, DateTime expires)
    {
        var creds = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expires,
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
