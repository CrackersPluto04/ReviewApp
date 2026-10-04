using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ReviewReplyController : ControllerBase
{
    private readonly IReviewReplyService _replyService;
    private readonly IUserAuthHelper _userAuthHelper;

    public ReviewReplyController(IReviewReplyService replyService, IUserAuthHelper userAuthHelper)
    {
        _replyService = replyService;
        _userAuthHelper = userAuthHelper;
    }

    [HttpGet]
    public async Task<IActionResult> GetReplies([FromQuery] int reviewId, [FromQuery] int? parentReplyId, [FromQuery] int? afterId, [FromQuery] int pageSize = 10)
    {
        var page = await _replyService.GetRepliesAsync(reviewId, parentReplyId, afterId, pageSize, _userAuthHelper.GetOptionalUserID());
        if (page == null) return NotFound(new { error = "Review not found." });

        return Ok(page);
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> CreateReply([FromBody] CreateReplyDto dto)
    {
        try
        {
            var result = await _replyService.CreateReplyAsync(_userAuthHelper.GetSecureUserID(), dto);
            if (result.Success)
                return Ok(result.Reply);
            else if (result.NotFound)
                return NotFound(new { error = result.Message });
            else
                return BadRequest(new { error = result.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    [HttpDelete("{id}")]
    [Authorize]
    public async Task<IActionResult> DeleteReply([FromRoute] int id)
    {
        try
        {
            var success = await _replyService.DeleteReplyAsync(_userAuthHelper.GetSecureUserID(), id);
            if (!success) return NotFound(new { error = "Reply not found." });

            return NoContent();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }
}
