using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Controllers;

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

    [HttpGet("media/{mediaType}/{externalApiId}")]
    public async Task<IActionResult> GetMediaReviews([FromRoute] MediaType mediaType, [FromRoute] string externalApiId, [FromQuery] ReviewFilterParams p)
    {
        var pagedReviews = await _reviewService.GetMediaReviewsAsync(mediaType, externalApiId, p);
        return Ok(pagedReviews);
    }

    [HttpGet("stats/average-score")]
    public async Task<IActionResult> GetAverageScore([FromQuery] string externalApiId, [FromQuery] MediaType mediaType)
    {
        var (AverageScore, ReviewCount) = await _reviewService.GetAverageScoreAsync(mediaType, externalApiId);
        return Ok(new { averageScore = AverageScore, reviewCount = ReviewCount });
    }

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
