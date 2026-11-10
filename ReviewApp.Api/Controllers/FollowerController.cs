using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Controllers;

/// <summary>
/// Follow relationships of the logged-in user: following, unfollowing and removing one of their own followers.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[Authorize]
public class FollowerController : ControllerBase
{
    private readonly IFollowerService _followerService;
    private readonly IUserAuthHelper _userAuthHelper;

    public FollowerController(IFollowerService followerService, IUserAuthHelper userAuthHelper)
    {
        _followerService = followerService;
        _userAuthHelper = userAuthHelper;
    }

    /// <summary>
    /// Makes the current user follow another user. The target is notified (at most once per actor in 7 days).
    /// </summary>
    /// <param name="targerUserId">ID of the user to follow.</param>
    /// <returns>A confirmation message.</returns>
    /// <response code="200">Now following.</response>
    /// <response code="400">The follow was refused, e.g. following oneself, an unknown user or an existing follow.</response>
    [HttpPost("{targerUserId}/follow")]
    public async Task<IActionResult> Follow([FromRoute] int targerUserId)
    {
        try
        {
            var (Success, Message) = await _followerService.FollowUserAsync(_userAuthHelper.GetSecureUserID(), targerUserId);
            if (Success)
                return Ok(new { message = Message });
            else
                return BadRequest(new { error = Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Stops the current user from following another user.
    /// </summary>
    /// <param name="targerUserId">ID of the user to unfollow.</param>
    /// <returns>A confirmation message.</returns>
    /// <response code="200">No longer following.</response>
    /// <response code="400">The current user was not following that user.</response>
    [HttpDelete("{targerUserId}/unfollow")]
    public async Task<IActionResult> Unfollow([FromRoute] int targerUserId)
    {
        try
        {
            var (Success, Message) = await _followerService.UnfollowUserAsync(_userAuthHelper.GetSecureUserID(), targerUserId);
            if (Success)
                return Ok(new { message = Message });
            else
                return BadRequest(new { error = Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Removes a follower from the current user's followers, without that user's involvement.
    /// </summary>
    /// <param name="followerToRemoveId">ID of the follower to remove.</param>
    /// <returns>A confirmation message.</returns>
    /// <response code="200">The follower was removed.</response>
    /// <response code="400">That user was not following the current user.</response>
    [HttpDelete("{followerToRemoveId}/remove")]
    public async Task<IActionResult> Remove([FromRoute] int followerToRemoveId)
    {
        try
        {
            var (Success, Message) = await _followerService.RemoveFollowerAsync(_userAuthHelper.GetSecureUserID(), followerToRemoveId);
            if (Success)
                return Ok(new { message = Message });
            else
                return BadRequest(new { error = Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }
}
