using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Controllers;

/// <summary>
/// Reviews of media. Reading is open to anonymous visitors, with each review's visibility decided per viewer; a
/// review that is hidden from the viewer answers 404. Writing needs a login and only touches the caller's own
/// reviews. There is at most one review per user and title.
/// </summary>
[Route("api/[controller]")]
[ApiController]
public class ReviewController : ControllerBase
{
    private readonly IReviewService _reviewService;
    private readonly IMediaService _mediaService;
    private readonly IUserAuthHelper _userAuthHelper;

    public ReviewController(IReviewService reviewService, IMediaService mediaService, IUserAuthHelper userAuthHelper)
    {
        _reviewService = reviewService;
        _mediaService = mediaService;
        _userAuthHelper = userAuthHelper;
    }

    /// <summary>
    /// Lists the reviews of one title that the viewer may see, filtered and paged.
    /// </summary>
    /// <param name="mediaType">Movie, series or music.</param>
    /// <param name="externalApiId">The TMDb or Spotify ID.</param>
    /// <param name="p">Filter, sort and paging parameters, from the query string.</param>
    /// <returns>A page of reviews.</returns>
    /// <response code="200">The page.</response>
    [HttpGet("media/{mediaType}/{externalApiId}")]
    public async Task<IActionResult> GetMediaReviews([FromRoute] MediaType mediaType, [FromRoute] string externalApiId, [FromQuery] ReviewFilterParams p)
    {
        var pagedReviews = await _reviewService.GetMediaReviewsAsync(mediaType, externalApiId, p);
        return Ok(pagedReviews);
    }

    /// <summary>
    /// Lists the newest public reviews that have written text. Feeds the Android home page.
    /// </summary>
    /// <param name="count">How many to return, 1 to 20.</param>
    /// <returns>The reviews, newest first, each with its author and media.</returns>
    /// <response code="200">The reviews.</response>
    [HttpGet("recent")]
    public async Task<IActionResult> GetRecentReviews([FromQuery] int count = 10)
    {
        var reviews = await _reviewService.GetRecentPublicReviewsAsync(count);
        return Ok(reviews);
    }

    /// <summary>
    /// Returns a single review with its author. This is the shareable review link and the target of reply notifications.
    /// </summary>
    /// <param name="id">The review's ID.</param>
    /// <returns>The review.</returns>
    /// <response code="200">The review.</response>
    /// <response code="404">The review does not exist or is hidden from the viewer. Both look the same on purpose.</response>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetReview([FromRoute] int id)
    {
        var review = await _reviewService.GetReviewByIdAsync(id, _userAuthHelper.GetOptionalUserID());
        if (review == null) return NotFound(new { error = "Review not found." });

        return Ok(review);
    }

    /// <summary>
    /// Returns the average score and review count of one title. Media cards request it one by one, so it is kept
    /// separate from the review list.
    /// </summary>
    /// <param name="externalApiId">The TMDb or Spotify ID.</param>
    /// <param name="mediaType">Movie, series or music.</param>
    /// <returns><c>{ averageScore, reviewCount }</c>.</returns>
    /// <response code="200">The statistics.</response>
    [HttpGet("stats/average-score")]
    public async Task<IActionResult> GetAverageScore([FromQuery] string externalApiId, [FromQuery] MediaType mediaType)
    {
        var (AverageScore, ReviewCount) = await _reviewService.GetAverageScoreAsync(mediaType, externalApiId);
        return Ok(new { averageScore = AverageScore, reviewCount = ReviewCount });
    }

    /// <summary>
    /// Creates the caller's review of a title. The title is fetched from TMDb / Spotify and cached locally the
    /// first time anyone uses it. May unlock achievements, reported in the <c>X-Unlocked-Achievements</c> response header.
    /// </summary>
    /// <param name="request">The title and the review's score, text and visibility.</param>
    /// <returns>A confirmation message.</returns>
    /// <response code="200">Created.</response>
    /// <response code="400">Invalid review, or the caller has already reviewed this title.</response>
    /// <response code="404">The source does not know this title (<c>media_not_found</c>).</response>
    /// <response code="503">TMDb / Spotify could not be reached (<c>source_unavailable</c>).</response>
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> CreateReview([FromBody] ReviewMediaDto request)
    {
        try
        {
            var (Success, Message) = await _reviewService.CreateReviewAsync(_userAuthHelper.GetSecureUserID(), request);
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
    /// Edits the caller's existing review of a title. The review is found by user and title, so the request has
    /// the same shape as a create.
    /// </summary>
    /// <param name="request">The title and the review's new values.</param>
    /// <returns>A confirmation message.</returns>
    /// <response code="200">Updated.</response>
    /// <response code="400">Invalid review, or the caller has no review of this title.</response>
    [HttpPut]
    [Authorize]
    public async Task<IActionResult> EditReview([FromBody] ReviewMediaDto request)
    {
        try
        {
            var (Success, Message) = await _reviewService.EditReviewAsync(_userAuthHelper.GetSecureUserID(), request);
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
    /// Deletes one of the caller's reviews together with all of its replies.
    /// </summary>
    /// <param name="id">The review's ID.</param>
    /// <response code="204">Deleted.</response>
    /// <response code="404">The caller has no such review.</response>
    [HttpDelete("{id}")]
    [Authorize]
    public async Task<IActionResult> DeleteReview(int id)
    {
        try
        {
            var (Success, Message) = await _reviewService.DeleteReviewAsync(_userAuthHelper.GetSecureUserID(), id);
            if (Success)
                return NoContent();
            else
                return NotFound(new { error = Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Tells whether the caller has reviewed a title and returns that review, so the review form can start in
    /// create or edit mode.
    /// </summary>
    /// <param name="externalApiId">The TMDb or Spotify ID.</param>
    /// <param name="mediaType">Movie, series or music.</param>
    /// <returns><c>{ hasReviewed, reviewData }</c>.</returns>
    /// <response code="200">The check result.</response>
    [HttpGet("check")]
    [Authorize]
    public async Task<IActionResult> CheckIfUserReviewedMedia([FromQuery] string externalApiId, [FromQuery] MediaType mediaType)
    {
        try
        {
            var (HasReviewed, Review) = await _reviewService.CheckIfUserReviewedMediaAsync(_userAuthHelper.GetSecureUserID(), mediaType, externalApiId);
            return Ok(new { hasReviewed = HasReviewed, reviewData = Review });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }
}
