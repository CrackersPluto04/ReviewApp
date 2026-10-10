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
    private const int NotificationSnippetLength = 80;

    private readonly AppDbContext _context;
    private readonly IAchievementService _achievementService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<ReviewReplyService> _logger;

    public ReviewReplyService(AppDbContext context, IAchievementService achievementService, INotificationService notificationService, ILogger<ReviewReplyService> logger)
    {
        _context = context;
        _achievementService = achievementService;
        _notificationService = notificationService;
        _logger = logger;
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
        var replies = await ToReplyDtos(query.OrderBy(rr => rr.ID).Take(pageSize + 1), requestingUserId)
            .ToListAsync();

        var hasMore = replies.Count > pageSize;

        return new ReplyPageDto
        {
            Items = replies.Take(pageSize).ToList(),
            HasMore = hasMore
        };
    }

    public async Task<ReplyContextDto?> GetReplyContextAsync(int replyId, int? requestingUserId)
    {
        var reply = await _context.ReviewReplies
            .Where(rr => rr.ID == replyId)
            .Select(rr => new { rr.ReviewID, rr.ParentReplyID, rr.IsDeleted })
            .FirstOrDefaultAsync();

        // A deleted reply can't be linked to
        if (reply == null || reply.IsDeleted)
            return null;

        // Replies inherit the visibility of their review
        if (await GetVisibleReviewAsync(reply.ReviewID, requestingUserId) == null)
            return null;

        var ids = reply.ParentReplyID.HasValue ? new[] { replyId, reply.ParentReplyID.Value } : [replyId];
        var dtos = await ToReplyDtos(_context.ReviewReplies.Where(rr => ids.Contains(rr.ID)), requestingUserId)
            .ToListAsync();

        return new ReplyContextDto
        {
            ReviewID = reply.ReviewID,
            Reply = dtos.First(d => d.ID == replyId),
            ParentReply = reply.ParentReplyID.HasValue ? dtos.FirstOrDefault(d => d.ID == reply.ParentReplyID.Value) : null
        };
    }

    // Content and author of deleted replies are never returned
    private static IQueryable<ReplyDto> ToReplyDtos(IQueryable<ReviewReply> query, int? requestingUserId) =>
        query.Select(rr => new ReplyDto
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
        });

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

        await NotifyReplyRecipientAsync(review, reply, author.Username);

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

        return true;
    }

    // Notifies the author of what was replied to: the review (layer 2 reply) or the parent reply (layer 3 reply)
    private async Task NotifyReplyRecipientAsync(Review review, ReviewReply reply, string actorUsername)
    {
        try
        {
            int recipientId;
            if (reply.ParentReplyID == null)
                recipientId = review.UserID;
            else
            {
                var parent = await _context.ReviewReplies
                    .Where(rr => rr.ID == reply.ParentReplyID.Value)
                    .Select(rr => new { rr.UserID, rr.IsDeleted })
                    .FirstAsync();

                // A deleted reply no longer has an author to notify
                if (parent.IsDeleted)
                    return;

                recipientId = parent.UserID;
            }

            // Never about your own actions, and only if the recipient can still see the review
            if (recipientId == reply.UserID || await GetVisibleReviewAsync(review.ID, recipientId) == null)
                return;

            var mediaTitle = await _context.Media
                .Where(m => m.ID == review.MediaID)
                .Select(m => m.Title)
                .FirstAsync();

            var snippet = reply.Content.Length <= NotificationSnippetLength
                ? reply.Content
                : reply.Content[..NotificationSnippetLength].TrimEnd() + "…";

            var isReviewReply = reply.ParentReplyID == null;
            await _notificationService.CreateAsync(new Notification
            {
                RecipientUserID = recipientId,
                ActorUserID = reply.UserID,
                Type = isReviewReply ? NotificationType.ReviewReply : NotificationType.ReplyReply,
                ReviewID = review.ID,
                ReplyID = reply.ID,
                Title = isReviewReply ? "New reply to your review" : "New reply to your reply",
                Message = isReviewReply
                    ? $"{actorUsername} replied to your review of {mediaTitle}: \"{snippet}\""
                    : $"{actorUsername} replied to you under a review of {mediaTitle}: \"{snippet}\""
            });
        }
        catch (Exception ex)
        {
            // The reply is already saved, a missing notification must not fail it
            _logger.LogError(ex, "Creating a reply notification for reply {ReplyId} failed", reply.ID);
        }
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
