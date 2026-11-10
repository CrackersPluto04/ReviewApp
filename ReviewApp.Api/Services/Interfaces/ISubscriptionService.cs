using ReviewApp.Api.DTOs;
using ReviewApp.Api.Services.Payments;

namespace ReviewApp.Api.Services.Interfaces;

/// <summary>
/// The Premium subscription. The local record only changes from what the payment provider reports; the decisions
/// themselves are in the pure <c>SubscriptionRules</c>.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Describes the Premium plan: price and features.</summary>
    PlanDto GetPlan();

    /// <summary>Returns the user's subscription state from the local record. A user who never started a checkout is Free.</summary>
    Task<SubscriptionStatusDto> GetStatusAsync(int userId);

    /// <summary>Starts a checkout.</summary>
    /// <param name="userId">The user who wants to subscribe.</param>
    /// <param name="android">True when the Android app asks, which decides the return URL.</param>
    /// <returns>Success, a message, and <c>Url</c>, the provider's hosted page to send the user to.</returns>
    Task<(bool Success, string Message, string? Url)> StartCheckoutAsync(int userId, bool android);

    /// <summary>Creates a customer portal session for payment details and invoices.</summary>
    /// <param name="userId">The subscriber.</param>
    /// <param name="android">True when the Android app asks, which decides the return URL.</param>
    Task<(bool Success, string Message, string? Url)> CreatePortalAsync(int userId, bool android);

    /// <summary>Stops renewing; Premium stays until the period end. Harmless when repeated.</summary>
    Task<(bool Success, string Message)> CancelAsync(int userId);

    /// <summary>Undoes a cancellation made in the same period. Harmless when repeated.</summary>
    Task<(bool Success, string Message)> ResumeAsync(int userId);

    /// <summary>Asks the provider for the current state and applies it, after returning from checkout or when a webhook was missed.</summary>
    Task<SubscriptionStatusDto> SyncAsync(int userId);

    /// <summary>
    /// Handles a provider webhook. The event is only a trigger to fetch the current subscription, so handling is
    /// idempotent and independent of event order.
    /// </summary>
    /// <returns>False when the notification is not authentic.</returns>
    /// <exception cref="Exception">Processing failed; the exception propagates so the provider redelivers the event.</exception>
    Task<bool> HandleWebhookAsync(string payload, string? signature);

    /// <summary>
    /// Safety net for subscriptions whose "ended" notification never arrived: re-checks lapsed running subscriptions
    /// with the provider and ends them if needed.
    /// </summary>
    /// <returns>How many subscriptions were ended.</returns>
    Task<int> ExpireLapsedAsync(CancellationToken cancellationToken);

    /// <summary>Development only: plays a provider event against the user's Fake subscription.</summary>
    Task<(bool Success, string Message)> SimulateAsync(int userId, FakeSubscriptionEvent simulated);
}
