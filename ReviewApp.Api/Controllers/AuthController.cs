using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ReviewApp.Api.Data;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace ReviewApp.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _configuration;

    public AuthController(AppDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }


    [HttpPost("register")]
    public async Task<IActionResult> Register(UserRegisterDto request)
    {
        if (await _context.Users.AnyAsync(u => u.Username == request.Username))
        {
            return BadRequest(new { error = "Username already exists." });
        }

        if (await _context.Users.AnyAsync(u => u.Email == request.Email))
        {
            return BadRequest(new { error = "User with this email already exists." });
        }

        string passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

        var newUser = new User
        {
            Username = request.Username,
            Email = request.Email,
            PasswordHash = passwordHash
        };

        _context.Users.Add(newUser);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Registration successfull! You can login now!" });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(UserLoginDto request)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            return Unauthorized(new { error = "Invalid email or password." });
        }

        string token = CreateToken(user);

        return Ok(new { token = token, message = "Login successful." });
    }


    [Authorize]
    [HttpGet("vip-lounge")]
    public IActionResult EnterVipLounge()
    {
        var userId = User.FindFirst("id")?.Value;
        var username = User.FindFirst("username")?.Value;

        return Ok(new { message = $"Welcome to the VIP lounge, {username} (User ID: {userId})!" });
    }


    private string CreateToken(User user)
    {
        var claims = new[]
        {
            new Claim("id", user.ID.ToString()),
            new Claim("username", user.Username),
            new Claim("email", user.Email)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]));

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
