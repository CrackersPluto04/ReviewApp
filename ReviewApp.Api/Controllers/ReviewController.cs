using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services;

namespace ReviewApp.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ReviewController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IMediaService mediaService;

    public ReviewController(AppDbContext context, IMediaService mediaService)
    {
        _context = context;
        this.mediaService = mediaService;
    }

    [HttpGet("media")]
    public async Task<IActionResult> GetMediaReviews([FromQuery] string externalApiId, [FromQuery] MediaType mediaType)
    {
        // Find Media ID, if null, nobody has reviewed it yet
        var media = await _context.Media.FirstOrDefaultAsync(m => m.ExternalApiID == externalApiId && m.MediaType == mediaType);
        if (media == null)
        {
            return Ok(new List<object>());
        }

        // Get public reviews for media
        var reviews = await _context.Reviews
            .Include(r => r.User)
            .Where(r => r.MediaID == media.ID && r.VisibilityLevel == VisibilityLevel.Public)
            .Select(r => new
            {
                r.Score,
                r.ReviewText,
                r.Pros,
                r.Cons,
                r.User.Username,
                r.User.ProfilePictureUrl,
                r.CreatedAt,
                r.UpdatedAt
            })
            .ToListAsync();

        return Ok(reviews);
    }

    [HttpGet("stats/average-score")]
    public async Task<IActionResult> GetAverageScore([FromQuery] string externalApiId, [FromQuery] MediaType mediaType)
    {
        // Find Media ID, if null, nobody has reviewed it yet
        var media = await _context.Media.FirstOrDefaultAsync(m => m.ExternalApiID == externalApiId && m.MediaType == mediaType);
        if (media == null)
        {
            return Ok(new { averageScore = 0, reviewCount = 0 });
        }

        // Calculate average score
        var publicReviews = _context.Reviews.Where(r => r.MediaID == media.ID && r.VisibilityLevel == VisibilityLevel.Public);
        var count = await publicReviews.CountAsync();
        var average = count > 0 ? await publicReviews.AverageAsync(r => r.Score) : 0;

        return Ok(new
        {
            averageScore = Math.Round(average, 1),
            reviewCount = count
        });
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> CreateReview([FromBody] ReviewMediaDto request)
    {
        // Identify the User and get userId
        var userIdClaim = User.Claims.FirstOrDefault(c => c.Type == "id");
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
        {
            return Unauthorized(new { error = "Invalid user token." });
        }

        // Check if media exists, if not add it to the database and get ID
        var mediaId = await mediaService.AddMediaAsync(request.MediaDto);
        if (mediaId == -1)
        {
            return BadRequest(new { error = "Invalid media type. Failed to add media." });
        }

        // Check if user has reviewed this media already
        if (await _context.Reviews.AnyAsync(r => r.UserID == userId && r.MediaID == mediaId))
        {
            return BadRequest(new { error = "Media already reviewed, please use edit instead." });
        }

        // Create new review
        var review = new Review
        {
            UserID = userId,
            MediaID = mediaId,
            Score = request.ReviewDto.Score,
            ReviewText = request.ReviewDto.ReviewText,
            Pros = request.ReviewDto.Pros,
            Cons = request.ReviewDto.Cons,
            VisibilityLevel = request.ReviewDto.VisibilityLevel
        };
        _context.Reviews.Add(review);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Review created successfully!" });
    }

    [HttpPut]
    [Authorize]
    public async Task<IActionResult> EditReview([FromBody] ReviewMediaDto request)
    {
        // Identify the User and get userId
        var userIdClaim = User.Claims.FirstOrDefault(c => c.Type == "id");
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
        {
            return Unauthorized(new { error = "Invalid user token." });
        }

        // Check if media exists
        var media = await _context.Media.FirstOrDefaultAsync(m => m.ExternalApiID == request.MediaDto.ExternalApiID && m.MediaType == request.MediaDto.MediaType);
        if (media == null)
        {
            return NotFound(new { error = "Media not found. Invalid media type or API ID." });
        }

        // Check if user has not reviewed this media already
        var review = await _context.Reviews.FirstOrDefaultAsync(r => r.UserID == userId && r.MediaID == media.ID);
        if (review == null)
        {
            return NotFound(new { error = "Review not found, please use create instead." });
        }

        // Update existing review
        review.Score = request.ReviewDto.Score;
        review.ReviewText = request.ReviewDto.ReviewText;
        review.Pros = request.ReviewDto.Pros;
        review.Cons = request.ReviewDto.Cons;
        review.VisibilityLevel = request.ReviewDto.VisibilityLevel;

        await _context.SaveChangesAsync();

        return Ok(new { message = "Review edited successfully!" });
    }

    [HttpGet("check")]
    [Authorize]
    public async Task<IActionResult> CheckIfUserReviewedMedia([FromQuery] string externalApiId, [FromQuery] MediaType mediaType)
    {
        // Identify the User and get userId
        var userIdClaim = User.Claims.FirstOrDefault(c => c.Type == "id");
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
        {
            return Unauthorized(new { error = "Invalid user token." });
        }

        // Check if media exists, if not there can be no review
        var media = await _context.Media.FirstOrDefaultAsync(m => m.ExternalApiID == externalApiId && m.MediaType == mediaType);
        if (media == null)
        {
            return Ok(new { hasReviewed = false });
        }

        // Check if user has reviewed this media already
        var review = await _context.Reviews.FirstOrDefaultAsync(r => r.UserID == userId && r.MediaID == media.ID);
        if (review == null)
        {
            return Ok(new { hasReviewed = false });
        }

        // Return review data if user has reviewed
        var reviewDto = new ReviewDto
        {
            Score = review.Score,
            ReviewText = review.ReviewText,
            Pros = review.Pros,
            Cons = review.Cons,
            VisibilityLevel = review.VisibilityLevel
        };

        return Ok(new { hasReviewed = true, reviewData = reviewDto });
    }
}
