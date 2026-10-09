using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Services;

public class ReviewService : IReviewService
{
    private readonly AppDbContext _context;
    private readonly IMediaService _mediaService;
    private readonly IAchievementService _achievementService;

    public ReviewService(AppDbContext context, IMediaService mediaService, IAchievementService achievementService)
    {
        _context = context;
        _mediaService = mediaService;
        _achievementService = achievementService;
    }

    public async Task<PagedResponse<object>> GetMediaReviewsAsync(MediaType mediaType, string externalApiId, ReviewFilterParams p)
    {
        // Find Media ID, if null, nobody has reviewed it yet
        var media = await _context.Media.FirstOrDefaultAsync(m => m.ExternalApiID == externalApiId && m.MediaType == mediaType);
        if (media == null)
        {
            return new PagedResponse<object>([], 0, p.Page, p.PageSize);
        }

        // Get public reviews for media
        var query = _context.Reviews
            .Include(r => r.User)
            .Where(r => r.MediaID == media.ID && r.VisibilityLevel == VisibilityLevel.Public);

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
                r.Score,
                r.ReviewText,
                r.Pros,
                r.Cons,
                r.User.Username,
                r.User.ProfilePictureUrl,
                r.CreatedAt,
                r.UpdatedAt,
                ReplyCount = _context.ReviewReplies.Count(rr => rr.ReviewID == r.ID && !rr.IsDeleted)
            })
            .ToListAsync();

        var response = new PagedResponse<object>(reviews, reviewsCount, p.Page, p.PageSize);
        return response;
    }

    public async Task<(decimal AverageScore, int ReviewCount)> GetAverageScoreAsync(MediaType mediaType, string externalApiId)
    {
        // Find Media ID, if null, nobody has reviewed it yet
        var media = await _context.Media.FirstOrDefaultAsync(m => m.ExternalApiID == externalApiId && m.MediaType == mediaType);
        if (media == null)
        {
            return (0, 0);
        }

        // Calculate average score
        var publicReviews = _context.Reviews.Where(r => r.MediaID == media.ID && r.VisibilityLevel == VisibilityLevel.Public);
        var count = await publicReviews.CountAsync();
        var average = count > 0 ? await publicReviews.AverageAsync(r => r.Score) : 0;

        return (Math.Round(average, 1), count);
    }

    public async Task<(bool Success, string Message)> CreateReviewAsync(int userId, ReviewMediaDto dto)
    {
        // Check if media exists, if not add it to the database and get ID
        var mediaId = await _mediaService.GetOrCreateMediaAsync(dto.MediaDto.MediaType, dto.MediaDto.ExternalApiID);
        if (mediaId == -1)
        {
            return (false, "Invalid media type. Failed to add media.");
        }

        // Check if user has reviewed this media already
        if (await _context.Reviews.AnyAsync(r => r.UserID == userId && r.MediaID == mediaId))
        {
            return (false, "Media already reviewed, please use edit instead.");
        }

        // Create new review
        var review = new Review
        {
            UserID = userId,
            MediaID = mediaId,
            Score = dto.ReviewDto.Score,
            ReviewText = dto.ReviewDto.ReviewText,
            Pros = dto.ReviewDto.Pros,
            Cons = dto.ReviewDto.Cons,
            VisibilityLevel = dto.ReviewDto.VisibilityLevel
        };
        _context.Reviews.Add(review);
        await _context.SaveChangesAsync();

        await _achievementService.EvaluateAsync(userId, MediaReviewMetric(dto.MediaDto.MediaType),
            AchievementMetric.TotalReviews, AchievementMetric.LowScoreReviews);

        return (true, "Review created successfully!");
    }

    public async Task<(bool Success, string Message)> EditReviewAsync(int userId, ReviewMediaDto dto)
    {
        // Check if media exists
        var media = await _context.Media.FirstOrDefaultAsync(m => m.ExternalApiID == dto.MediaDto.ExternalApiID && m.MediaType == dto.MediaDto.MediaType);
        if (media == null)
        {
            return (false, "Media not found. Invalid media type or API ID.");
        }

        // Check if user has not reviewed this media already
        var review = await _context.Reviews.FirstOrDefaultAsync(r => r.UserID == userId && r.MediaID == media.ID);
        if (review == null)
        {
            return (false, "Review not found, please use create instead.");
        }

        // Update existing review
        review.Score = dto.ReviewDto.Score;
        review.ReviewText = dto.ReviewDto.ReviewText;
        review.Pros = dto.ReviewDto.Pros;
        review.Cons = dto.ReviewDto.Cons;
        review.VisibilityLevel = dto.ReviewDto.VisibilityLevel;

        await _context.SaveChangesAsync();

        // The score may have crossed the low score limit
        await _achievementService.EvaluateAsync(userId, AchievementMetric.LowScoreReviews);

        return (true, "Review edited successfully!");
    }

    public async Task<(bool Success, string Message)> DeleteReviewAsync(int userId, int reviewId)
    {
        // Check if review exists
        var review = await _context.Reviews.FirstOrDefaultAsync(r => r.UserID == userId && r.ID == reviewId);
        if (review == null)
            return (false, "Review not found.");

        // Delete review if exists
        _context.Reviews.Remove(review);
        await _context.SaveChangesAsync();

        return (true, "Review deleted successfully!");
    }

    private static AchievementMetric MediaReviewMetric(MediaType mediaType) => mediaType switch
    {
        MediaType.Movie => AchievementMetric.MovieReviews,
        MediaType.Series => AchievementMetric.SeriesReviews,
        _ => AchievementMetric.MusicReviews
    };

    public async Task<(bool HasReviewed, ReviewDto? Review)> CheckIfUserReviewedMediaAsync(int userId, MediaType mediaType, string externalApiId)
    {
        // Check if media exists, if not there can be no review
        var media = await _context.Media.FirstOrDefaultAsync(m => m.ExternalApiID == externalApiId && m.MediaType == mediaType);
        if (media == null)
        {
            return (false, null);
        }

        // Check if user has reviewed this media already
        var review = await _context.Reviews.FirstOrDefaultAsync(r => r.UserID == userId && r.MediaID == media.ID);
        if (review == null)
        {
            return (false, null);
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

        return (true, reviewDto);
    }

    public async Task<(bool Success, PagedResponse<object>? Reviews)> GetUserReviewsAsync(string username, int? requestingUserId, ReviewFilterParams p)
    {
        // Find target user by username
        var targetUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (targetUser == null)
            return (false, null);

        // Check if requesting user is the owner of the collections
        // or a follower of the owner
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
                IsOwner = isOwner,
                ReplyCount = _context.ReviewReplies.Count(rr => rr.ReviewID == r.ID && !rr.IsDeleted)
            })
            .ToListAsync();

        var response = new PagedResponse<object>(reviews, reviewsCount, p.Page, p.PageSize);
        return (true, response);
    }
}
