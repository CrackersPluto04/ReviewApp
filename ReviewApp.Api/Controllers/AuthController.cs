using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services;
using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Controllers;

/// <summary>
/// Registration, login (including the second step of a two-factor login), logout and the session check.
/// The session is an httpOnly cookie issued through <see cref="ITokenService"/>; clients never see the token.
/// Login and registration share the per-IP <c>auth-attempt</c> rate limit.
/// </summary>
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
    private readonly IEntitlementService _entitlementService;

    public AuthController(AppDbContext context, IUserAuthHelper userAuthHelper, ITokenService tokenService,
        ISecurityService securityService, IEntitlementService entitlementService)
    {
        _context = context;
        _userAuthHelper = userAuthHelper;
        _tokenService = tokenService;
        _securityService = securityService;
        _entitlementService = entitlementService;
    }

    /// <summary>
    /// Creates an account and sends the address verification email. Does not log the user in.
    /// </summary>
    /// <param name="request">Username, email and password; the first two are trimmed.</param>
    /// <returns>A confirmation message.</returns>
    /// <response code="200">Registered; the verification email is on its way.</response>
    /// <response code="400">Invalid username (see <c>UsernameRules</c>), or the username or email is taken.</response>
    /// <response code="429">The <c>auth-attempt</c> rate limit was hit.</response>
    [HttpPost("register")]
    [EnableRateLimiting("auth-attempt")]
    public async Task<IActionResult> Register([FromBody] UserRegisterDto request)
    {
        var username = request.Username.Trim();
        var email = request.Email.Trim();

        if (UsernameRules.Validate(username) is { } usernameError)
        {
            return BadRequest(new { error = usernameError });
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

    /// <summary>
    /// Logs in with email and password. Accounts with two-factor authentication get no session yet: a code is
    /// emailed and a short-lived challenge token returned for <see cref="LoginTwoFactor"/>.
    /// </summary>
    /// <param name="request">Email and password.</param>
    /// <returns>The logged-in user, or <c>{ twoFactorRequired, challengeToken }</c> for a two-factor account.</returns>
    /// <response code="200">Logged in (session cookie set), or the second step is required.</response>
    /// <response code="401">Wrong email or password. The two cases are indistinguishable.</response>
    /// <response code="429">The <c>auth-attempt</c> rate limit was hit.</response>
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

        return Ok(new { message = "Logged in successfully", user = await ToAuthUserAsync(user) });
    }

    /// <summary>
    /// Second step of a two-factor login. The session cookie is only issued here.
    /// </summary>
    /// <param name="request">The challenge token from login and the emailed code.</param>
    /// <returns>The logged-in user.</returns>
    /// <response code="200">Logged in (session cookie set).</response>
    /// <response code="401">The challenge expired or was revoked, or the code is wrong.</response>
    /// <response code="429">The <c>auth-attempt</c> rate limit was hit.</response>
    [HttpPost("login/2fa")]
    [EnableRateLimiting("auth-attempt")]
    public async Task<IActionResult> LoginTwoFactor([FromBody] TwoFactorLoginDto request)
    {
        var (Success, Message, User) = await _securityService.CompleteTwoFactorLoginAsync(request.ChallengeToken, request.Code);
        if (!Success)
            return Unauthorized(new { error = Message });

        _tokenService.IssueAuthCookie(User!);

        return Ok(new { message = Message, user = await ToAuthUserAsync(User!) });
    }

    /// <summary>
    /// Emails a new login code for a pending two-factor login. Resends have a 60 second cooldown.
    /// </summary>
    /// <param name="request">The challenge token from login.</param>
    /// <returns>A confirmation message.</returns>
    /// <response code="200">A new code was sent.</response>
    /// <response code="400">The challenge is invalid or expired, or the cooldown has not passed.</response>
    /// <response code="429">The <c>email-send</c> rate limit was hit.</response>
    [HttpPost("login/2fa/resend")]
    [EnableRateLimiting("email-send")]
    public async Task<IActionResult> ResendTwoFactorCode([FromBody] TwoFactorResendDto request)
    {
        var (Success, Message) = await _securityService.ResendTwoFactorLoginCodeAsync(request.ChallengeToken);
        if (!Success)
            return BadRequest(new { error = Message });

        return Ok(new { message = Message });
    }

    /// <summary>
    /// Clears this browser's session cookie. Other devices stay logged in; see <see cref="LogoutAll"/>.
    /// </summary>
    /// <response code="200">Logged out.</response>
    [HttpPost("logout")]
    public IActionResult Logout()
    {
        _tokenService.DeleteAuthCookie();
        return Ok(new { message = "Logged out successfully" });
    }

    /// <summary>
    /// Logs the user out of every device, including this one, by bumping <c>TokenVersion</c>, which invalidates
    /// all tokens issued so far.
    /// </summary>
    /// <response code="200">All sessions revoked.</response>
    /// <response code="401">Not logged in, or the user no longer exists.</response>
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

    /// <summary>
    /// Returns the logged-in user, including tier and entitlements. Clients call it on start-up to restore their
    /// session and again after anything that can change those fields, such as a subscription change.
    /// </summary>
    /// <returns>The user as the clients store it: ID, username, picture, email confirmation, tier and entitlements.</returns>
    /// <response code="200">The current user.</response>
    /// <response code="401">No valid session.</response>
    /// <response code="404">The user behind the session no longer exists.</response>
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

            return Ok(await ToAuthUserAsync(user));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    // The logged-in user as the frontend's AuthContext stores it. The tier and features are read fresh
    // here; clients refresh this after subscription changes.
    private async Task<object> ToAuthUserAsync(User user)
    {
        var tier = await _entitlementService.GetTierAsync(user.ID);
        return new
        {
            id = user.ID,
            username = user.Username,
            profilePictureUrl = user.ProfilePictureUrl,
            isEmailConfirmed = user.IsEmailConfirmed,
            tier = tier.ToString(),
            isPremium = tier == SubscriptionTier.Premium,
            entitlements = Entitlements.FeaturesFor(tier)
        };
    }
}
