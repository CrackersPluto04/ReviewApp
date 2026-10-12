using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.DTOs;

namespace ReviewApp.Api.Services.Interfaces;

public interface INotificationService
{
    // Creation never throws: a failed notification is logged and must not break the action that triggered it
    Task CreateAsync(Notification notification);
    Task CreateSystemAsync(int recipientUserId, string title, string message);
    Task NotifyNewFollowerAsync(int followerId, int followingId);

    Task<NotificationPageDto> GetNotificationsAsync(int userId, NotificationQueryParams p);
    Task<int> GetUnseenCountAsync(int userId);
    Task<bool> SetSeenAsync(int userId, int notificationId, bool isSeen);
    Task<int> MarkAllSeenAsync(int userId);
    Task<bool> DeleteAsync(int userId, int notificationId);
    Task<int> DeleteSeenAsync(int userId);
}
