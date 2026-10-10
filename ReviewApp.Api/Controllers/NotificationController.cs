using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Controllers;

// Every endpoint works on the current user's own notifications only
[Route("api/[controller]")]
[ApiController]
[Authorize]
public class NotificationController : ControllerBase
{
    private readonly INotificationService _notificationService;
    private readonly IUserAuthHelper _userAuthHelper;

    public NotificationController(INotificationService notificationService, IUserAuthHelper userAuthHelper)
    {
        _notificationService = notificationService;
        _userAuthHelper = userAuthHelper;
    }

    [HttpGet]
    public async Task<IActionResult> GetNotifications([FromQuery] NotificationQueryParams p)
    {
        try
        {
            return Ok(await _notificationService.GetNotificationsAsync(_userAuthHelper.GetSecureUserID(), p));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    // Lightweight, polled by the header badge
    [HttpGet("unseen-count")]
    public async Task<IActionResult> GetUnseenCount()
    {
        try
        {
            var count = await _notificationService.GetUnseenCountAsync(_userAuthHelper.GetSecureUserID());
            return Ok(new { count });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    [HttpPatch("{id:int}/seen")]
    public async Task<IActionResult> SetSeen([FromRoute] int id, [FromBody] SetNotificationSeenDto dto)
    {
        try
        {
            var success = await _notificationService.SetSeenAsync(_userAuthHelper.GetSecureUserID(), id, dto.IsSeen);
            if (!success) return NotFound(new { error = "Notification not found." });

            return NoContent();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    [HttpPost("seen-all")]
    public async Task<IActionResult> MarkAllSeen()
    {
        try
        {
            var updated = await _notificationService.MarkAllSeenAsync(_userAuthHelper.GetSecureUserID());
            return Ok(new { updated });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete([FromRoute] int id)
    {
        try
        {
            var success = await _notificationService.DeleteAsync(_userAuthHelper.GetSecureUserID(), id);
            if (!success) return NotFound(new { error = "Notification not found." });

            return NoContent();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    [HttpDelete("seen")]
    public async Task<IActionResult> DeleteSeen()
    {
        try
        {
            var deleted = await _notificationService.DeleteSeenAsync(_userAuthHelper.GetSecureUserID());
            return Ok(new { deleted });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }
}
