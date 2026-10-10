using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Services;

// Stores a notification for every newly unlocked tier. Unlike the response header handler,
// this one also covers users whose progress changed during someone else's request.
public class NotificationUnlockHandler : IAchievementUnlockHandler
{
    private readonly INotificationService _notificationService;

    public NotificationUnlockHandler(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public async Task HandleAsync(int userId, IReadOnlyList<UnlockedAchievementDto> unlocked)
    {
        foreach (var achievement in unlocked)
        {
            await _notificationService.CreateAsync(new Notification
            {
                RecipientUserID = userId,
                Type = NotificationType.AchievementUnlocked,
                GroupCode = achievement.GroupCode,
                Title = "Achievement unlocked",
                Message = $"You earned the {achievement.Tier} tier of {achievement.Title}."
            });
        }
    }
}
