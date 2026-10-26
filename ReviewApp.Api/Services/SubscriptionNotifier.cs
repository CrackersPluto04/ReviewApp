using System.Globalization;
using ReviewApp.Api.DAL;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Services;

public static class SubscriptionText
{
    // Dates in emails and notifications (stored as text) are UTC, e.g. "November 25, 2026"
    public static string Date(DateTime utc) => utc.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
}

public class SubscriptionNotifier : ISubscriptionNotifier
{
    private readonly AppDbContext _context;
    private readonly INotificationService _notificationService;
    private readonly IAccountEmailService _accountEmailService;
    private readonly ILogger<SubscriptionNotifier> _logger;

    public SubscriptionNotifier(AppDbContext context, INotificationService notificationService,
        IAccountEmailService accountEmailService, ILogger<SubscriptionNotifier> logger)
    {
        _context = context;
        _notificationService = notificationService;
        _accountEmailService = accountEmailService;
        _logger = logger;
    }

    public async Task NotifyAsync(int userId, SubscriptionTransitions transitions, SubscriptionState state)
    {
        try
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null) return;

            var accessEndsAt = SubscriptionRules.AccessEndsAt(state);
            var until = accessEndsAt is { } date ? $" until {SubscriptionText.Date(date)}" : "";

            if (transitions.HasFlag(SubscriptionTransitions.Started))
            {
                await Notify(userId, NotificationType.SubscriptionStarted, "Welcome to Premium",
                    "Your Premium subscription is active. Thanks for supporting the app!");
                _accountEmailService.SendPremiumStarted(user.Email, user.Username, accessEndsAt);
            }

            if (transitions.HasFlag(SubscriptionTransitions.PaymentFailed))
            {
                await Notify(userId, NotificationType.PaymentFailed, "Payment failed",
                    $"We couldn't renew your Premium subscription. Update your payment details to keep Premium; it stays active{until}.");
                _accountEmailService.SendPaymentFailed(user.Email, user.Username, accessEndsAt);
            }

            if (transitions.HasFlag(SubscriptionTransitions.Cancelled))
            {
                await Notify(userId, NotificationType.SubscriptionCancelled, "Subscription cancelled",
                    $"Your Premium subscription won't renew. You keep Premium{until}.");
                _accountEmailService.SendSubscriptionCancelled(user.Email, user.Username, accessEndsAt);
            }

            if (transitions.HasFlag(SubscriptionTransitions.Ended))
            {
                await Notify(userId, NotificationType.SubscriptionEnded, "Premium has ended",
                    "Your account is on the Free plan now. You can subscribe again any time.");
                _accountEmailService.SendSubscriptionEnded(user.Email, user.Username);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to notify user {UserId} about subscription change {Transitions}", userId, transitions);
        }
    }

    private Task Notify(int userId, NotificationType type, string title, string message) =>
        _notificationService.CreateAsync(new Notification
        {
            RecipientUserID = userId,
            Type = type,
            Title = title,
            Message = message
        });
}
