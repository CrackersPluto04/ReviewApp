using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Controllers;

/// <summary>
/// Email verification, email change confirmation and the two-factor authentication settings.
/// The two-factor login steps live in <see cref="AuthController"/>, next to the normal login.
/// </summary>
[Route("api/[controller]")]
[ApiController]
public class SecurityController : ControllerBase
{
    private readonly ISecurityService _securityService;
    private readonly IUserAuthHelper _userAuthHelper;
    private readonly ITokenService _tokenService;

    public SecurityController(ISecurityService securityService, IUserAuthHelper userAuthHelper, ITokenService tokenService)
    {
        _securityService = securityService;
        _userAuthHelper = userAuthHelper;
        _tokenService = tokenService;
    }

    /// <summary>
    /// Returns the caller's security state: email, whether it is verified, a pending email change and whether
    /// two-factor authentication is on. This is the only place the clients read the email and the pending
    /// address from.
    /// </summary>
    /// <returns>The security status.</returns>
    /// <response code="200">The status.</response>
    /// <response code="404">The user no longer exists.</response>
    [HttpGet("status")]
    [Authorize]
    public async Task<IActionResult> GetStatus()
    {
        try
        {
            var status = await _securityService.GetStatusAsync(_userAuthHelper.GetSecureUserID());
            if (status == null) return NotFound(new { error = "User not found." });

            return Ok(status);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Confirms an email address from the token in the emailed link. Anonymous, because the link can be opened in
    /// any browser. It is a POST sent by the frontend page rather than the link's GET, so mail scanners that
    /// prefetch links cannot confirm anything.
    /// </summary>
    /// <param name="dto">The token from the link.</param>
    /// <returns>A confirmation message.</returns>
    /// <response code="200">Verified.</response>
    /// <response code="400">The token is invalid, expired or already used.</response>
    /// <response code="429">The <c>email-link</c> rate limit was hit.</response>
    [HttpPost("verify-email")]
    [EnableRateLimiting("email-link")]
    public async Task<IActionResult> VerifyEmail([FromBody] EmailLinkTokenDto dto)
    {
        var (Success, Message) = await _securityService.ConfirmEmailAsync(dto.Token);
        if (!Success)
            return BadRequest(new { error = Message });

        return Ok(new { message = Message });
    }

    /// <summary>
    /// Emails a new verification link, invalidating older unused ones. Resends have a 60 second cooldown.
    /// </summary>
    /// <returns>A confirmation message.</returns>
    /// <response code="200">Sent.</response>
    /// <response code="400">Already verified, or the cooldown has not passed.</response>
    /// <response code="429">The <c>email-send</c> rate limit was hit.</response>
    [HttpPost("resend-verification")]
    [Authorize]
    [EnableRateLimiting("email-send")]
    public async Task<IActionResult> ResendVerification()
    {
        try
        {
            var (Success, Message) = await _securityService.ResendVerificationAsync(_userAuthHelper.GetSecureUserID());
            if (!Success)
                return BadRequest(new { error = Message });

            return Ok(new { message = Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Completes an email change from the token mailed to the new address. Anonymous and POST-only for the same
    /// reasons as <see cref="VerifyEmail"/>. Confirming revokes every session (<c>TokenVersion</c> is bumped).
    /// </summary>
    /// <param name="dto">The token from the link.</param>
    /// <returns><c>{ message, loggedIn }</c>; <c>loggedIn</c> says whether this browser kept a session.</returns>
    /// <response code="200">The email was changed.</response>
    /// <response code="400">The token is invalid, expired or already used.</response>
    /// <response code="429">The <c>email-link</c> rate limit was hit.</response>
    [HttpPost("confirm-email-change")]
    [EnableRateLimiting("email-link")]
    public async Task<IActionResult> ConfirmEmailChange([FromBody] EmailLinkTokenDto dto)
    {
        var (Success, Message, User) = await _securityService.ConfirmEmailChangeAsync(dto.Token);
        if (!Success)
            return BadRequest(new { error = Message });

        // Every session was revoked. If the link was opened in a browser logged in as the same user, keep that one logged in.
        var loggedIn = _userAuthHelper.GetOptionalUserID() == User!.ID;
        if (loggedIn)
            _tokenService.IssueAuthCookie(User);

        return Ok(new { message = Message, loggedIn });
    }

    /// <summary>
    /// Cancels the caller's pending email change; the emailed link stops working.
    /// </summary>
    /// <response code="204">Cancelled.</response>
    /// <response code="404">There is no pending email change.</response>
    [HttpDelete("pending-email")]
    [Authorize]
    public async Task<IActionResult> CancelEmailChange()
    {
        try
        {
            var success = await _securityService.CancelEmailChangeAsync(_userAuthHelper.GetSecureUserID());
            if (!success) return NotFound(new { error = "There is no pending email change." });

            return NoContent();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    /// <summary>
    /// First step of enabling two-factor authentication: checks the password and emails a code. Needs a verified email.
    /// </summary>
    /// <param name="dto">The caller's current password.</param>
    /// <returns>A confirmation message.</returns>
    /// <response code="200">A code was sent.</response>
    /// <response code="400">Wrong password, unverified email, or already enabled.</response>
    /// <response code="429">The <c>credential-change</c> rate limit was hit.</response>
    [HttpPost("2fa/enable/request")]
    [Authorize]
    [EnableRateLimiting("credential-change")]
    public Task<IActionResult> RequestEnableTwoFactor([FromBody] CurrentPasswordDto dto) =>
        RequestTwoFactorChange(true, dto.CurrentPassword);

    /// <summary>
    /// Second step of enabling two-factor authentication: checks the emailed code. Uses the per-user
    /// <c>credential-change</c> limit rather than the per-IP login limit, so mistyped codes here never block logging in.
    /// Codes burn after 5 wrong tries.
    /// </summary>
    /// <param name="dto">The emailed code.</param>
    /// <returns>A confirmation message.</returns>
    /// <response code="200">Two-factor authentication is on.</response>
    /// <response code="400">Wrong, expired or burned code.</response>
    /// <response code="429">The <c>credential-change</c> rate limit was hit.</response>
    [HttpPost("2fa/enable/confirm")]
    [Authorize]
    [EnableRateLimiting("credential-change")]
    public Task<IActionResult> ConfirmEnableTwoFactor([FromBody] TwoFactorCodeDto dto) =>
        ConfirmTwoFactorChange(true, dto.Code);

    /// <summary>
    /// First step of disabling two-factor authentication: checks the password and emails a code.
    /// </summary>
    /// <param name="dto">The caller's current password.</param>
    /// <returns>A confirmation message.</returns>
    /// <response code="200">A code was sent.</response>
    /// <response code="400">Wrong password, or two-factor authentication is not enabled.</response>
    /// <response code="429">The <c>credential-change</c> rate limit was hit.</response>
    [HttpPost("2fa/disable/request")]
    [Authorize]
    [EnableRateLimiting("credential-change")]
    public Task<IActionResult> RequestDisableTwoFactor([FromBody] CurrentPasswordDto dto) =>
        RequestTwoFactorChange(false, dto.CurrentPassword);

    /// <summary>
    /// Second step of disabling two-factor authentication: checks the emailed code.
    /// </summary>
    /// <param name="dto">The emailed code.</param>
    /// <returns>A confirmation message.</returns>
    /// <response code="200">Two-factor authentication is off.</response>
    /// <response code="400">Wrong, expired or burned code.</response>
    /// <response code="429">The <c>credential-change</c> rate limit was hit.</response>
    [HttpPost("2fa/disable/confirm")]
    [Authorize]
    [EnableRateLimiting("credential-change")]
    public Task<IActionResult> ConfirmDisableTwoFactor([FromBody] TwoFactorCodeDto dto) =>
        ConfirmTwoFactorChange(false, dto.Code);

    private async Task<IActionResult> RequestTwoFactorChange(bool enable, string currentPassword)
    {
        try
        {
            var (Success, Message) = await _securityService.RequestTwoFactorChangeAsync(_userAuthHelper.GetSecureUserID(), enable, currentPassword);
            if (!Success)
                return BadRequest(new { error = Message });

            return Ok(new { message = Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    private async Task<IActionResult> ConfirmTwoFactorChange(bool enable, string code)
    {
        try
        {
            var (Success, Message) = await _securityService.ConfirmTwoFactorChangeAsync(_userAuthHelper.GetSecureUserID(), enable, code);
            if (!Success)
                return BadRequest(new { error = Message });

            return Ok(new { message = Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }
}
