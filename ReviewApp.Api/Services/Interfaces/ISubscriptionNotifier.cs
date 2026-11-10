namespace ReviewApp.Api.Services.Interfaces;

/// <summary>
/// Tells the user about subscription changes with an in-app notification and an email. Never throws: a failed
/// notification must not undo the subscription change that caused it.
/// </summary>
public interface ISubscriptionNotifier
{
    /// <summary>Notifies about each transition that happened.</summary>
    /// <param name="userId">The subscriber.</param>
    /// <param name="transitions">What changed, found by comparing the stored state before and after.</param>
    /// <param name="state">The subscription state after the change, used for the dates in the messages.</param>
    Task NotifyAsync(int userId, SubscriptionTransitions transitions, SubscriptionState state);
}
