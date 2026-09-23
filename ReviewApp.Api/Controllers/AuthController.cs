using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IUserAuthHelper _userAuthHelper;
    private readonly ITokenService _tokenService;

    public AuthController(AppDbContext context, IUserAuthHelper userAuthHelper, ITokenService tokenService)
    {
        _context = context;
        _userAuthHelper = userAuthHelper;
        _tokenService = tokenService;
    }

    [HttpPost("register")]
    [EnableRateLimiting("auth-attempt")]
    public async Task<IActionResult> Register([FromBody] UserRegisterDto request)
    {
        var username = request.Username.Trim();
        var email = request.Email.Trim();

        if (username.Length < 3 || username.Length > 20)
        {
            return BadRequest(new { error = "Username must be between 3 and 20 characters." });
        }

        if (await _context.Users.AnyAsync(u => u.Username == username))
        {
            return BadRequest(new { error = "Username already exists." });
        }

        if (await _context.Users.AnyAsync(u => u.Email == email))
        {
            return BadRequest(new { error = "User with this email already exists." });
        }

        string passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

        var newUser = new User
        {
            Username = username,
            Email = email,
            PasswordHash = passwordHash
        };

        _context.Users.Add(newUser);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Unique index violation from a concurrent registration with the same username / email
            if (await _context.Users.AnyAsync(u => u.Username == username))
                return BadRequest(new { error = "Username already exists." });

            if (await _context.Users.AnyAsync(u => u.Email == email))
                return BadRequest(new { error = "User with this email already exists." });

            throw;
        }

        return Ok(new { message = "Registration successfull! You can login now." });
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth-attempt")]
    public async Task<IActionResult> Login([FromBody] UserLoginDto request)
    {
        var email = request.Email.Trim();
        
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            return Unauthorized(new { error = "Invalid email or password." });
        }

        _tokenService.IssueAuthCookie(user);

        return Ok(new
        {
            message = "Logged in successfully",
            user = new
            {
                id = user.ID,
                username = user.Username,
                profilePictureUrl = user.ProfilePictureUrl
            }
        });
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        Response.Cookies.Delete("jwt_token");
        return Ok(new { message = "Logged out successfully" });
    }

    [HttpGet("check-auth")]
    [Authorize]
    public async Task<IActionResult> CheckAuth()
    {
        try
        {
            var userId = _userAuthHelper.GetSecureUserID();

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return NotFound();

            return Ok(new
            {
                id = user.ID,
                username = user.Username,
                profilePictureUrl = user.ProfilePictureUrl
            });
        }
        catch (Exception ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }
}
