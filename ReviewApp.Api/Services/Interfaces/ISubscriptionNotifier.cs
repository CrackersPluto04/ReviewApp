namespace ReviewApp.Api.Services.Interfaces;

// Tells the user about subscription changes (in-app notification + email). Never throws.
public interface ISubscriptionNotifier
{
    Task NotifyAsync(int userId, SubscriptionTransitions transitions, SubscriptionState state);
}
