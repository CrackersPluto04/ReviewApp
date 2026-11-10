using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Controllers;

/// <summary>
/// The logged-in user's notifications. Every endpoint works on the caller's own notifications only: someone else's
/// ID answers 404, the same as one that does not exist.
/// </summary>
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

    /// <summary>
    /// Lists the caller's notifications with keyset paging, so "load more" stays stable while new ones arrive.
    /// </summary>
    /// <param name="p">Seen / unseen filter, sort order and the <c>afterId</c> cursor, from the query string.</param>
    /// <returns>One page of notifications.</returns>
    /// <response code="200">The page.</response>
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

    /// <summary>
    /// Counts the caller's unseen notifications. Kept separate from the list because the clients poll it for the
    /// bell badge, so it must stay cheap.
    /// </summary>
    /// <returns><c>{ count }</c>.</returns>
    /// <response code="200">The unseen count.</response>
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

    /// <summary>
    /// Marks one notification seen or unseen.
    /// </summary>
    /// <param name="id">The notification's ID.</param>
    /// <param name="dto">The seen state to set.</param>
    /// <response code="204">Updated.</response>
    /// <response code="404">The caller has no such notification.</response>
    [HttpPatch("{id:int}/seen")]
    public async Task<IActionResult> SetSeen([FromRoute] int id, [FromBody] SetNotificationSeenDto dto)
    {
        try
        {
            var success = await _notificationService.SetSeenAsync(_userAuthHelper.GetSecureUserID(), id, dto.IsSeen!.Value);
            if (!success) return NotFound(new { error = "Notification not found." });

            return NoContent();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Marks every unseen notification of the caller as seen.
    /// </summary>
    /// <returns><c>{ updated }</c>, the number of notifications changed.</returns>
    /// <response code="200">Done.</response>
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

    /// <summary>
    /// Deletes one notification.
    /// </summary>
    /// <param name="id">The notification's ID.</param>
    /// <response code="204">Deleted.</response>
    /// <response code="404">The caller has no such notification.</response>
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

    /// <summary>
    /// Deletes all of the caller's seen notifications. Unseen ones are kept so nothing unread disappears.
    /// </summary>
    /// <returns><c>{ deleted }</c>, the number of notifications removed.</returns>
    /// <response code="200">Done.</response>
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
