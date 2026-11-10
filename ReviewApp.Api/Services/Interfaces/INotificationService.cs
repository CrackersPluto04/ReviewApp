using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.DTOs;

namespace ReviewApp.Api.Services.Interfaces;

/// <summary>
/// The user's notifications. A user keeps at most 200; the oldest seen ones are dropped first. Every method takes
/// the owner's ID, so someone else's notification is reported as missing.
/// </summary>
public interface INotificationService
{
    /// <summary>Stores a notification. Never throws: a failure is logged, because a failed notification must not break the action that triggered it.</summary>
    Task CreateAsync(Notification notification);

    /// <summary>Stores a system notification with ready-to-display text.</summary>
    Task CreateSystemAsync(int recipientUserId, string title, string message);

    /// <summary>Tells a user about a new follower, at most once per actor in 7 days. Never throws.</summary>
    Task NotifyNewFollowerAsync(int followerId, int followingId);

    /// <summary>Lists notifications with keyset paging.</summary>
    Task<NotificationPageDto> GetNotificationsAsync(int userId, NotificationQueryParams p);

    /// <summary>Counts unseen notifications. Cheap, because the clients poll it for the badge.</summary>
    Task<int> GetUnseenCountAsync(int userId);

    /// <summary>Marks one notification seen or unseen.</summary>
    /// <returns>False when the user has no such notification.</returns>
    Task<bool> SetSeenAsync(int userId, int notificationId, bool isSeen);

    /// <summary>Returns the newest notifications.</summary>
    /// <param name="userId">The recipient.</param>
    /// <param name="count">How many to return, capped at the per-user maximum.</param>
    Task<List<NotificationDto>> GetLatestAsync(int userId, int count = 200);

    /// <summary>Marks every unseen notification as seen.</summary>
    /// <param name="userId">The recipient.</param>
    /// <param name="upToId">Limits it to notifications up to this ID. The offline sync uses it so only the notifications the device had seen are touched.</param>
    /// <returns>How many were changed.</returns>
    Task<int> MarkAllSeenAsync(int userId, int? upToId = null);

    /// <summary>Deletes one notification.</summary>
    /// <returns>False when the user has no such notification.</returns>
    Task<bool> DeleteAsync(int userId, int notificationId);

    /// <summary>Deletes all seen notifications; unseen ones are kept.</summary>
    /// <param name="userId">The recipient.</param>
    /// <param name="upToId">Limits it to notifications up to this ID, as for <see cref="MarkAllSeenAsync"/>.</param>
    /// <returns>How many were deleted.</returns>
    Task<int> DeleteSeenAsync(int userId, int? upToId = null);
}
