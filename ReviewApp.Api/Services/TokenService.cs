using Microsoft.IdentityModel.Tokens;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.Services.Interfaces;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace ReviewApp.Api.Services;

public class TokenService : ITokenService
{
    private readonly IConfiguration _configuration;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public TokenService(IConfiguration configuration, IHttpContextAccessor httpContextAccessor)
    {
        _configuration = configuration;
        _httpContextAccessor = httpContextAccessor;
    }

    public void IssueAuthCookie(User user)
    {
        var response = _httpContextAccessor.HttpContext?.Response
            ?? throw new InvalidOperationException("No active HTTP response to write the auth cookie to.");

        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTime.UtcNow.AddHours(8)
        };

        response.Cookies.Append("jwt_token", CreateToken(user), cookieOptions);
    }

    private string CreateToken(User user)
    {
        var claims = new[]
        {
            new Claim("id", user.ID.ToString()),
            new Claim("username", user.Username),
            new Claim("email", user.Email),
            new Claim("ver", user.TokenVersion.ToString())
        };

        var jwtKey = _configuration["Jwt:Key"];
        if (string.IsNullOrEmpty(jwtKey))
        {
            throw new InvalidOperationException("JWT key is not configured.");
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.Now.AddHours(8),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
