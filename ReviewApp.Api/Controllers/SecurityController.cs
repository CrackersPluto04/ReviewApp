using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Controllers;

// Email verification, email change confirmation and 2FA settings.
// The 2FA login steps live in AuthController, next to the normal login.
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

    // Anonymous: the link can be opened in any browser. A POST from the page, so a link scanner's GET can't confirm anything.
    [HttpPost("verify-email")]
    [EnableRateLimiting("email-link")]
    public async Task<IActionResult> VerifyEmail([FromBody] EmailLinkTokenDto dto)
    {
        var (Success, Message) = await _securityService.ConfirmEmailAsync(dto.Token);
        if (!Success)
            return BadRequest(new { error = Message });

        return Ok(new { message = Message });
    }

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

    [HttpPost("2fa/enable/request")]
    [Authorize]
    [EnableRateLimiting("credential-change")]
    public Task<IActionResult> RequestEnableTwoFactor([FromBody] CurrentPasswordDto dto) =>
        RequestTwoFactorChange(true, dto.CurrentPassword);

    // Per user (not the per-IP login limit), so mistyped codes here never block logging in
    [HttpPost("2fa/enable/confirm")]
    [Authorize]
    [EnableRateLimiting("credential-change")]
    public Task<IActionResult> ConfirmEnableTwoFactor([FromBody] TwoFactorCodeDto dto) =>
        ConfirmTwoFactorChange(true, dto.Code);

    [HttpPost("2fa/disable/request")]
    [Authorize]
    [EnableRateLimiting("credential-change")]
    public Task<IActionResult> RequestDisableTwoFactor([FromBody] CurrentPasswordDto dto) =>
        RequestTwoFactorChange(false, dto.CurrentPassword);

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
