using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace ReviewApp.Api.Tests.Support;

// Hand-made JWTs, for the cases TokenService itself never produces (expired, wrong key, wrong audience)
// and for logging test users in without going through the login endpoint.
public static class TestTokens
{
    public static IConfiguration Configuration { get; } = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = ApiFactory.JwtKey,
            ["Jwt:Issuer"] = ApiFactory.JwtIssuer,
            ["Jwt:Audience"] = ApiFactory.JwtAudience,
            ["App:FrontendBaseUrl"] = ApiFactory.FrontendBaseUrl
        })
        .Build();

    public static string Create(IEnumerable<Claim> claims, string? audience = ApiFactory.JwtAudience, DateTime? expires = null,
        string key = ApiFactory.JwtKey, string issuer = ApiFactory.JwtIssuer, DateTime? notBefore = null)
    {
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256);
        var expiresAt = expires ?? DateTime.UtcNow.AddHours(1);
        var token = new JwtSecurityToken(issuer, audience, claims,
            notBefore: notBefore ?? expiresAt.AddHours(-2), expires: expiresAt, signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // A valid session token, the same shape TokenService issues
    public static string Session(int userId, int tokenVersion = 0, DateTime? expires = null) =>
        Create([new Claim("id", userId.ToString()), new Claim("ver", tokenVersion.ToString())], expires: expires);

    public static IReadOnlyDictionary<string, string> ReadClaims(string token) =>
        new JwtSecurityTokenHandler().ReadJwtToken(token).Claims
            .GroupBy(c => c.Type)
            .ToDictionary(g => g.Key, g => g.First().Value);
}
