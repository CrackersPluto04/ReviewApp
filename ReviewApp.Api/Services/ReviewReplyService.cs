using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Services;

public class ReviewReplyService : IReviewReplyService
{
    private const int MaxPageSize = 50;

    private readonly AppDbContext _context;
    private readonly IAchievementService _achievementService;

    public ReviewReplyService(AppDbContext context, IAchievementService achievementService)
    {
        _context = context;
        _achievementService = achievementService;
    }

    public async Task<ReplyPageDto?> GetRepliesAsync(int reviewId, int? parentReplyId, int? afterId, int pageSize, int? requestingUserId)
    {
        // Replies inherit the visibility of their review, hidden review == not found
        var review = await GetVisibleReviewAsync(reviewId, requestingUserId);
        if (review == null)
            return null;

        // Parent reply must belong to the same review
        if (parentReplyId.HasValue && !await _context.ReviewReplies.AnyAsync(rr => rr.ID == parentReplyId.Value && rr.ReviewID == reviewId))
            return null;

        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        // Direct replies of the review (parentReplyId == null) or of the given reply.
        // Deleted replies only stay visible as placeholders while they still have non-deleted children.
        var query = _context.ReviewReplies
            .Where(rr => rr.ReviewID == reviewId && rr.ParentReplyID == parentReplyId)
            .Where(rr => !rr.IsDeleted || rr.ChildReplies.Any(c => !c.IsDeleted));

        if (afterId.HasValue)
            query = query.Where(rr => rr.ID > afterId.Value);

        // Oldest first, fetch one extra to know if there are more
        var replies = await query
            .OrderBy(rr => rr.ID)
            .Take(pageSize + 1)
            .Select(rr => new ReplyDto
            {
                ID = rr.ID,
                ParentReplyID = rr.ParentReplyID,
                Content = rr.IsDeleted ? null : rr.Content,
                Username = rr.IsDeleted ? null : rr.User.Username,
                ProfilePictureUrl = rr.IsDeleted ? null : rr.User.ProfilePictureUrl,
                CreatedAt = rr.CreatedAt.ToString("yyyy-MM-dd"),
                IsDeleted = rr.IsDeleted,
                IsOwner = !rr.IsDeleted && requestingUserId.HasValue && rr.UserID == requestingUserId.Value,
                ChildCount = rr.ChildReplies.Count(c => !c.IsDeleted)
            })
            .ToListAsync();

        var hasMore = replies.Count > pageSize;

        return new ReplyPageDto
        {
            Items = replies.Take(pageSize).ToList(),
            HasMore = hasMore
        };
    }

    public async Task<(bool Success, bool NotFound, string Message, ReplyDto? Reply)> CreateReplyAsync(int userId, CreateReplyDto dto)
    {
        var content = dto.Content.Trim();
        if (content.Length == 0)
            return (false, false, "Reply cannot be empty.", null);

        // Check if the review exists and the user is allowed to see it
        var review = await GetVisibleReviewAsync(dto.ReviewID, userId);
        if (review == null)
            return (false, true, "Review not found.", null);

        if (review.VisibilityLevel == VisibilityLevel.Private)
            return (false, false, "Private reviews can't be replied to.", null);

        // Layer is derived from the parent chain: review -> reply -> reply to reply
        if (dto.ParentReplyID.HasValue)
        {
            var parent = await _context.ReviewReplies
                .Where(rr => rr.ID == dto.ParentReplyID.Value && rr.ReviewID == dto.ReviewID)
                .Select(rr => new { rr.ParentReplyID })
                .FirstOrDefaultAsync();

            if (parent == null)
                return (false, false, "The reply you are responding to was not found.", null);

            if (parent.ParentReplyID != null)
                return (false, false, "Replies can only be nested up to 3 levels.", null);
        }

        var reply = new ReviewReply
        {
            ReviewID = dto.ReviewID,
            UserID = userId,
            ParentReplyID = dto.ParentReplyID,
            Content = content
        };
        _context.ReviewReplies.Add(reply);
        await _context.SaveChangesAsync();

        await _achievementService.EvaluateAsync(userId, AchievementMetric.Replies);

        var author = await _context.Users
            .Where(u => u.ID == userId)
            .Select(u => new { u.Username, u.ProfilePictureUrl })
            .FirstAsync();

        var replyDto = new ReplyDto
        {
            ID = reply.ID,
            ParentReplyID = reply.ParentReplyID,
            Content = reply.Content,
            Username = author.Username,
            ProfilePictureUrl = author.ProfilePictureUrl,
            CreatedAt = reply.CreatedAt.ToString("yyyy-MM-dd"),
            IsDeleted = false,
            IsOwner = true,
            ChildCount = 0
        };

        return (true, false, "Reply created successfully!", replyDto);
    }

    public async Task<bool> DeleteReplyAsync(int userId, int replyId)
    {
        // Only the author can delete, already deleted replies count as not found
        var reply = await _context.ReviewReplies.FirstOrDefaultAsync(rr => rr.ID == replyId && rr.UserID == userId && !rr.IsDeleted);
        if (reply == null)
            return false;

        // Soft delete: the record (and its content) stays so child replies keep their parent
        reply.IsDeleted = true;
        await _context.SaveChangesAsync();

        await _achievementService.EvaluateAsync(userId, AchievementMetric.Replies);

        return true;
    }

    private async Task<Review?> GetVisibleReviewAsync(int reviewId, int? requestingUserId)
    {
        var review = await _context.Reviews.AsNoTracking().FirstOrDefaultAsync(r => r.ID == reviewId);
        if (review == null)
            return null;

        // Owner always sees their own review
        if (requestingUserId.HasValue && requestingUserId.Value == review.UserID)
            return review;

        if (review.VisibilityLevel == VisibilityLevel.Public)
            return review;

        // Followers-only reviews are visible to followers of the owner, private ones only to the owner
        if (review.VisibilityLevel == VisibilityLevel.FollowersOnly && requestingUserId.HasValue)
        {
            var isFollower = await _context.UserFollowers.AnyAsync(uf =>
                uf.FollowerID == requestingUserId.Value &&
                uf.FollowingID == review.UserID);

            if (isFollower)
                return review;
        }

        return null;
    }
}
