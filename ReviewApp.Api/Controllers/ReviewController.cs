using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.DTOs;

namespace ReviewApp.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ReviewController : ControllerBase
{
    private readonly AppDbContext _context;

    public ReviewController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet("media/{mediaID}")]
    public async Task<IActionResult> GetPublicReviewsForMedia(int mediaID)
    {
        var reviews = await _context.Reviews
            .Include(r => r.User)
            .Where(r => r.MediaID == mediaID && r.VisibilityLevel == Enums.VisibilityLevel.Public)
            .Select(r => new
            {
                r.ID,
                r.Score,
                r.ReviewText,
                r.Pros,
                r.Cons,
                r.CreatedAt,
                username = r.User!.Username
            })
            .ToListAsync();

        return Ok(reviews);
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> CreateReview(ReviewCreateDto request)
    {
        var userIdClaim = User.FindFirst("id")?.Value;
        if (userIdClaim == null || !int.TryParse(userIdClaim, out int userID))
        {
            return Unauthorized(new { error = "Invalid or missing token data." });
        }

        var mediaExists = await _context.Media.AnyAsync(m => m.ID == request.MediaID);
        if (!mediaExists)
        {
            return NotFound(new { error = "Media not found." });
        }

        var existingReview = await _context.Reviews
            .FirstOrDefaultAsync(r => r.UserID == userID && r.MediaID == request.MediaID);
        if (existingReview != null)
        {
            return BadRequest(new { error = "You have already reviewed this media." });
        }

        var newReview = new Review
        {
            UserID = userID,
            MediaID = request.MediaID,
            Score = request.Score,
            ReviewText = request.ReviewText,
            Pros = request.Pros,
            Cons = request.Cons,
            VisibilityLevel = request.VisibilityLevel
        };

        _context.Reviews.Add(newReview);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Review created successfully!", id = newReview.ID });
    }
}
