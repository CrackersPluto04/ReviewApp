using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReviewApp.Api.Services;

namespace ReviewApp.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class FollowerController : ControllerBase
{
    private readonly IFollowerService _followerService;

    public FollowerController(IFollowerService followerService)
    {
        _followerService = followerService;
    }

    [HttpPost("{targerUserId}/follow")]
    public async Task<IActionResult> Follow([FromRoute] int targerUserId)
    {
        try
        {
            var (Success, Message) = await _followerService.FollowUserAsync(GetSecureUserId(), targerUserId);
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

    [HttpDelete("{targerUserId}/unfollow")]
    public async Task<IActionResult> Unfollow([FromRoute] int targerUserId)
    {
        try
        {
            var (Success, Message) = await _followerService.UnfollowUserAsync(GetSecureUserId(), targerUserId);
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

    [HttpDelete("{followerToRemoveId}/remove")]
    public async Task<IActionResult> Remove([FromRoute] int followerToRemoveId)
    {
        try
        {
            var (Success, Message) = await _followerService.RemoveFollowerAsync(GetSecureUserId(), followerToRemoveId);
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

    /* Helper methods */

    // Helper method to extract user ID from JWT claims
    private int GetSecureUserId()
    {
        var userIdClaim = User.Claims.FirstOrDefault(c => c.Type == "id");
        if (userIdClaim != null && int.TryParse(userIdClaim.Value, out int userId))
        {
            return userId;
        }

        throw new UnauthorizedAccessException("Invalid user token.");
    }
}
