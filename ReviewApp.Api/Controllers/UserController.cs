using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services;

namespace ReviewApp.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class UserController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IUserService _userService;
    private readonly ICollectionService _collectionService;

    public UserController(AppDbContext context, IUserService userService, ICollectionService collectionService)
    {
        _context = context;
        _userService = userService;
        _collectionService = collectionService;
    }

    [HttpGet("search")]
    public async Task<IActionResult> SearchUsers([FromQuery] string q)
    {
        var users = await _userService.SearchUsersAsync(q);
        return Ok(users);
    }

    [HttpGet("{username}")]
    public async Task<IActionResult> GetUserProfile([FromRoute] string username)
    {
        var userProfile = await _userService.GetUserProfileAsync(username, GetOptionalUserId());
        if (userProfile == null)
            return NotFound(new { error = "User profile not found." });

        return Ok(userProfile);
    }

    [HttpPatch("me")]
    [Authorize]
    public async Task<IActionResult> UpdateMyProfile([FromBody] UserUpdateDto dto)
    {
        try
        {
            var (Success, Message) = await _userService.UpdateUserProfileAsync(GetSecureUserId(), dto);
            if (!Success)
                return BadRequest(new { error = Message });

            return Ok(new { message = Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    [HttpGet("{username}/collections")]
    public async Task<IActionResult> GetUserCollections([FromRoute] string username, [FromQuery] string sortBy = "createdAt_asc")
    {
        var collections = await _collectionService.GetUserCollectionsAsync(username, GetOptionalUserId(), sortBy);
        if (collections == null)
            return NotFound(new { error = "User profile not found." });

        return Ok(collections);
    }

    [HttpGet("{username}/reviews")]
    public async Task<IActionResult> GetUserReviews([FromRoute] string username, [FromQuery] ReviewFilterParams p)
    {
        // Find target user by username
        var targetUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (targetUser == null)
            return NotFound(new { error = "User profile not found." });

        // Check if requesting user is the owner of the collections
        // or a follower of the owner
        var requestingUserId = GetOptionalUserId();

        var isOwner = requestingUserId.HasValue && requestingUserId.Value == targetUser.ID;

        var isFollower = false;
        if (requestingUserId.HasValue && !isOwner)
            isFollower = await _context.UserFollowers.AnyAsync(uf =>
                uf.FollowerID == requestingUserId.Value &&
                uf.FollowingID == targetUser.ID);

        // Build the query to get reviews, applying visibility filter
        var query = _context.Reviews.Where(r => r.UserID == targetUser.ID);

        if (!isOwner)
        {
            if (isFollower)
                query = query.Where(r =>
                    r.VisibilityLevel == VisibilityLevel.Public ||
                    r.VisibilityLevel == VisibilityLevel.FollowersOnly);
            else
                query = query.Where(r => r.VisibilityLevel == VisibilityLevel.Public);
        }

        // Apply filters
        if (p.HasWrittenText)
            query = query.Where(r => !string.IsNullOrWhiteSpace(r.ReviewText) || !string.IsNullOrWhiteSpace(r.Pros) || !string.IsNullOrWhiteSpace(r.Cons));

        query = query.Where(r => r.Score >= p.MinScore);
        query = query.Where(r => r.Score <= p.MaxScore);

        query = p.SortBy switch
        {
            "created_asc" => query.OrderBy(r => r.CreatedAt),
            "updated_desc" => query.OrderByDescending(r => r.UpdatedAt),
            "updated_asc" => query.OrderBy(r => r.UpdatedAt),
            "score_desc" => query.OrderByDescending(r => r.Score),
            "score_asc" => query.OrderBy(r => r.Score),
            _ => query.OrderByDescending(r => r.CreatedAt)
        };

        // Count reviews and apply pagination
        var reviewsCount = await query.CountAsync();
        var reviews = await query
            .Skip((p.Page - 1) * p.PageSize)
            .Take(p.PageSize)
            .Select(r => new
            {
                r.ID,
                r.Media.Title,
                r.Media.PosterUrl,
                r.Media.MediaType,
                r.Media.ExternalApiID,
                r.Score,
                r.ReviewText,
                r.Pros,
                r.Cons,
                CreatedAt = r.CreatedAt.ToString("yyyy-MM-dd"),
                UpdatedAt = r.UpdatedAt.ToString("yyyy-MM-dd"),
                r.VisibilityLevel,
                IsOwner = isOwner
            })
            .ToListAsync();

        var response = new PagedResponse<object>(reviews, reviewsCount, p.Page, p.PageSize);
        return Ok(response);
    }

    [HttpGet("{username}/followers")]
    public async Task<IActionResult> GetUserFollowers([FromRoute] string username)
    {
        var followers = await _userService.GetUserFollowersAsync(username, GetOptionalUserId());
        return Ok(followers);
    }

    [HttpGet("{username}/following")]
    public async Task<IActionResult> GetUserFollowing([FromRoute] string username)
    {
        var following = await _userService.GetUserFollowingAsync(username, GetOptionalUserId());
        return Ok(following);
    }

    /* Helper methods */

    // Helper method to extract user ID from JWT claims, but throws an exception if not found or invalid
    private int GetSecureUserId()
    {
        var userIdClaim = User.Claims.FirstOrDefault(c => c.Type == "id");
        if (userIdClaim != null && int.TryParse(userIdClaim.Value, out int userId))
        {
            return userId;
        }

        throw new UnauthorizedAccessException("Invalid user token.");
    }

    // Helper method to extract user ID from JWT claims, but returns null if not found or invalid
    private int? GetOptionalUserId()
    {
        var userIdClaim = User.Claims.FirstOrDefault(c => c.Type == "id");
        if (userIdClaim != null && int.TryParse(userIdClaim.Value, out int userId))
        {
            return userId;
        }
        return null;
    }
}
