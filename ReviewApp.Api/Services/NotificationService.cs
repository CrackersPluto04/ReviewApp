using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Services;

public class NotificationService : INotificationService
{
    // Oldest seen notifications are dropped first when a user goes over the cap
    public const int MaxPerUser = 200;
    private const int MaxPageSize = 50;
    // A follow / unfollow / follow cycle doesn't notify again within this window
    private static readonly TimeSpan FollowerNotificationWindow = TimeSpan.FromDays(7);

    private readonly AppDbContext _context;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(AppDbContext context, ILogger<NotificationService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task CreateAsync(Notification notification)
    {
        notification.Title = Truncate(notification.Title, Notification.MaxTitleLength);
        notification.Message = Truncate(notification.Message, Notification.MaxMessageLength);

        try
        {
            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync();

            await EnforceCapAsync(notification.RecipientUserID);
        }
        catch (Exception ex)
        {
            // Must never break the action that triggered it (reply, follow, achievement unlock, account change)
            _logger.LogError(ex, "Creating a {Type} notification for user {UserId} failed", notification.Type, notification.RecipientUserID);
            _context.Entry(notification).State = EntityState.Detached;
        }
    }

    public Task CreateSystemAsync(int recipientUserId, string title, string message) =>
        CreateAsync(new Notification
        {
            RecipientUserID = recipientUserId,
            Type = NotificationType.System,
            Title = title,
            Message = message
        });

    public async Task NotifyNewFollowerAsync(int followerId, int followingId)
    {
        try
        {
            var since = DateTime.UtcNow - FollowerNotificationWindow;
            var alreadyNotified = await _context.Notifications.AnyAsync(n =>
                n.RecipientUserID == followingId &&
                n.ActorUserID == followerId &&
                n.Type == NotificationType.NewFollower &&
                n.CreatedAt >= since);
            if (alreadyNotified)
                return;

            var followerName = await _context.Users
                .Where(u => u.ID == followerId)
                .Select(u => u.Username)
                .FirstAsync();

            var isFollowBack = await _context.UserFollowers.AnyAsync(uf => uf.FollowerID == followingId && uf.FollowingID == followerId);

            await CreateAsync(new Notification
            {
                RecipientUserID = followingId,
                ActorUserID = followerId,
                Type = NotificationType.NewFollower,
                Title = "New follower",
                Message = isFollowBack ? $"{followerName} followed you back." : $"{followerName} started following you."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Creating a follower notification for user {UserId} failed", followingId);
        }
    }

    public async Task<NotificationPageDto> GetNotificationsAsync(int userId, NotificationQueryParams p)
    {
        var query = _context.Notifications.AsNoTracking().Where(n => n.RecipientUserID == userId);

        query = p.Status switch
        {
            "seen" => query.Where(n => n.IsSeen),
            "unseen" => query.Where(n => !n.IsSeen),
            _ => query
        };

        var totalCount = await query.CountAsync();
        var unseenCount = await GetUnseenCountAsync(userId);

        // IDs grow with the creation date, so they order by date and serve as the keyset cursor
        var oldestFirst = p.Sort == "oldest";
        if (p.AfterId.HasValue)
            query = oldestFirst ? query.Where(n => n.ID > p.AfterId.Value) : query.Where(n => n.ID < p.AfterId.Value);
        query = oldestFirst ? query.OrderBy(n => n.ID) : query.OrderByDescending(n => n.ID);

        var pageSize = Math.Clamp(p.PageSize, 1, MaxPageSize);

        // Fetch one extra to know if there are more
        var rows = await query
            .Take(pageSize + 1)
            .Select(n => new
            {
                n.ID,
                n.Type,
                n.Title,
                n.Message,
                n.IsSeen,
                n.CreatedAt,
                ActorUsername = n.ActorUser != null ? n.ActorUser.Username : null,
                ActorProfilePictureUrl = n.ActorUser != null ? n.ActorUser.ProfilePictureUrl : null,
                n.ReviewID,
                n.ReplyID,
                n.GroupCode
            })
            .ToListAsync();

        return new NotificationPageDto
        {
            Items = rows.Take(pageSize).Select(n => new NotificationDto
            {
                ID = n.ID,
                Type = n.Type.ToString(),
                Title = n.Title,
                Message = n.Message,
                IsSeen = n.IsSeen,
                // Stored as UTC, but read back as Unspecified, which would serialize without the "Z"
                CreatedAt = DateTime.SpecifyKind(n.CreatedAt, DateTimeKind.Utc),
                ActorUsername = n.ActorUsername,
                ActorProfilePictureUrl = n.ActorProfilePictureUrl,
                ReviewID = n.ReviewID,
                ReplyID = n.ReplyID,
                GroupCode = n.GroupCode
            }).ToList(),
            HasMore = rows.Count > pageSize,
            TotalCount = totalCount,
            UnseenCount = unseenCount
        };
    }

    public Task<int> GetUnseenCountAsync(int userId) =>
        _context.Notifications.CountAsync(n => n.RecipientUserID == userId && !n.IsSeen);

    // Filtering by recipient makes another user's notification behave as if it didn't exist
    public async Task<bool> SetSeenAsync(int userId, int notificationId, bool isSeen) =>
        await _context.Notifications
            .Where(n => n.ID == notificationId && n.RecipientUserID == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsSeen, isSeen)) > 0;

    public Task<int> MarkAllSeenAsync(int userId) =>
        _context.Notifications
            .Where(n => n.RecipientUserID == userId && !n.IsSeen)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsSeen, true));

    public async Task<bool> DeleteAsync(int userId, int notificationId) =>
        await _context.Notifications
            .Where(n => n.ID == notificationId && n.RecipientUserID == userId)
            .ExecuteDeleteAsync() > 0;

    public Task<int> DeleteSeenAsync(int userId) =>
        _context.Notifications
            .Where(n => n.RecipientUserID == userId && n.IsSeen)
            .ExecuteDeleteAsync();

    private async Task EnforceCapAsync(int userId)
    {
        var count = await _context.Notifications.CountAsync(n => n.RecipientUserID == userId);
        if (count <= MaxPerUser)
            return;

        // Seen ones first, then the oldest
        var excessIds = await _context.Notifications
            .Where(n => n.RecipientUserID == userId)
            .OrderByDescending(n => n.IsSeen)
            .ThenBy(n => n.ID)
            .Take(count - MaxPerUser)
            .Select(n => n.ID)
            .ToListAsync();

        await _context.Notifications.Where(n => excessIds.Contains(n.ID)).ExecuteDeleteAsync();
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";
}
