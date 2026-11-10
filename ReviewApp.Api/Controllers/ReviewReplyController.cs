using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Controllers;

/// <summary>
/// Reply threads under reviews, at most three layers: review, reply, reply to a reply. Replies have no visibility
/// of their own: every read and write checks the parent review, and a review hidden from the caller answers 404.
/// Reading is anonymous; writing needs a login.
/// </summary>
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

    /// <summary>
    /// Lists the replies of a review (layer 2) or of one reply (layer 3). Keyset paging, so "load more" stays
    /// correct while replies are added or deleted. A deleted reply that still has live children appears as a placeholder.
    /// </summary>
    /// <param name="reviewId">The review whose thread is read.</param>
    /// <param name="parentReplyId">Omitted for the review's direct replies, or the layer-2 reply whose children are wanted.</param>
    /// <param name="afterId">Cursor: the ID of the last reply of the previous page.</param>
    /// <param name="pageSize">How many replies to return.</param>
    /// <returns>A page of replies.</returns>
    /// <response code="200">The page.</response>
    /// <response code="404">The review does not exist or is hidden from the viewer.</response>
    [HttpGet]
    public async Task<IActionResult> GetReplies([FromQuery] int reviewId, [FromQuery] int? parentReplyId, [FromQuery] int? afterId, [FromQuery] int pageSize = 10)
    {
        var page = await _replyService.GetRepliesAsync(reviewId, parentReplyId, afterId, pageSize, _userAuthHelper.GetOptionalUserID());
        if (page == null) return NotFound(new { error = "Review not found." });

        return Ok(page);
    }

    /// <summary>
    /// Returns one reply together with its layer-2 parent, used to open a thread at a specific reply
    /// (the target of reply notifications).
    /// </summary>
    /// <param name="id">The reply's ID.</param>
    /// <returns>The reply and its parent.</returns>
    /// <response code="200">The reply.</response>
    /// <response code="404">The reply is deleted or does not exist, or its review is hidden from the viewer.</response>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetReplyContext([FromRoute] int id)
    {
        var context = await _replyService.GetReplyContextAsync(id, _userAuthHelper.GetOptionalUserID());
        if (context == null) return NotFound(new { error = "Reply not found." });

        return Ok(context);
    }

    /// <summary>
    /// Posts a reply to a review or to a layer-2 reply, and notifies the person replied to.
    /// </summary>
    /// <param name="dto">The review, the optional parent reply and the text.</param>
    /// <returns>The created reply.</returns>
    /// <response code="200">Created.</response>
    /// <response code="400">Invalid text, a reply to a layer-3 reply (the thread stops at three layers), or a private review.</response>
    /// <response code="404">The review or parent reply does not exist or is hidden from the caller.</response>
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

    /// <summary>
    /// Deletes the caller's own reply. The delete is soft: the text stays in the database but is never returned again.
    /// </summary>
    /// <param name="id">The reply's ID.</param>
    /// <response code="204">Deleted.</response>
    /// <response code="404">The caller has no such reply.</response>
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
