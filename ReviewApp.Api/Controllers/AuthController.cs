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
    // Hashed once, with the same work factor as real passwords, so a failed lookup costs the same as a wrong password
    private static readonly string DummyPasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString());

    private readonly AppDbContext _context;
    private readonly IUserAuthHelper _userAuthHelper;
    private readonly ITokenService _tokenService;
    private readonly ISecurityService _securityService;

    public AuthController(AppDbContext context, IUserAuthHelper userAuthHelper, ITokenService tokenService, ISecurityService securityService)
    {
        _context = context;
        _userAuthHelper = userAuthHelper;
        _tokenService = tokenService;
        _securityService = securityService;
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

        await _securityService.OnRegisteredAsync(newUser);

        return Ok(new { message = "Registration successful! We sent you an email to verify your address. You can log in now." });
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth-attempt")]
    public async Task<IActionResult> Login([FromBody] UserLoginDto request)
    {
        var email = request.Email.Trim();
        
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);

        // Unknown emails are checked against a dummy hash too, so the response time doesn't reveal which emails are registered
        var passwordValid = BCrypt.Net.BCrypt.Verify(request.Password, user?.PasswordHash ?? DummyPasswordHash);
        if (user == null || !passwordValid)
        {
            return Unauthorized(new { error = "Invalid email or password." });
        }

        // Only reached with a correct password, so the response doesn't reveal whether an account has 2FA
        if (user.IsTwoFactorEnabled)
        {
            var challengeToken = await _securityService.StartTwoFactorLoginAsync(user);
            return Ok(new
            {
                twoFactorRequired = true,
                challengeToken,
                message = "We sent a 6-digit code to your email address."
            });
        }

        _tokenService.IssueAuthCookie(user);

        return Ok(new { message = "Logged in successfully", user = ToAuthUser(user) });
    }

    // Second step of a 2FA login: the session cookie is only issued here
    [HttpPost("login/2fa")]
    [EnableRateLimiting("auth-attempt")]
    public async Task<IActionResult> LoginTwoFactor([FromBody] TwoFactorLoginDto request)
    {
        var (Success, Message, User) = await _securityService.CompleteTwoFactorLoginAsync(request.ChallengeToken, request.Code);
        if (!Success)
            return Unauthorized(new { error = Message });

        _tokenService.IssueAuthCookie(User!);

        return Ok(new { message = Message, user = ToAuthUser(User!) });
    }

    [HttpPost("login/2fa/resend")]
    [EnableRateLimiting("email-send")]
    public async Task<IActionResult> ResendTwoFactorCode([FromBody] TwoFactorResendDto request)
    {
        var (Success, Message) = await _securityService.ResendTwoFactorLoginCodeAsync(request.ChallengeToken);
        if (!Success)
            return BadRequest(new { error = Message });

        return Ok(new { message = Message });
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        _tokenService.DeleteAuthCookie();
        return Ok(new { message = "Logged out successfully" });
    }

    [HttpPost("logout-all")]
    [Authorize]
    public async Task<IActionResult> LogoutAll()
    {
        try
        {
            var user = await _context.Users.FindAsync(_userAuthHelper.GetSecureUserID());
            if (user == null)
                return Unauthorized();

            // Revokes every token issued so far, on every device
            user.TokenVersion++;
            await _context.SaveChangesAsync();

            _tokenService.DeleteAuthCookie();
            return Ok(new { message = "Logged out of all devices." });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
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

            return Ok(ToAuthUser(user));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    // The logged-in user as the frontend's AuthContext stores it
    private static object ToAuthUser(User user) => new
    {
        id = user.ID,
        username = user.Username,
        profilePictureUrl = user.ProfilePictureUrl,
        isEmailConfirmed = user.IsEmailConfirmed
    };
}
