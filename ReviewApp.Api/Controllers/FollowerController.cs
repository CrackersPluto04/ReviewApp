using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Controllers;

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
