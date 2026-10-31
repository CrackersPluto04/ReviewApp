using ReviewApp.Api.DTOs;
using ReviewApp.Api.Services.Payments;

namespace ReviewApp.Api.Services.Interfaces;

public interface ISubscriptionService
{
    PlanDto GetPlan();
    Task<SubscriptionStatusDto> GetStatusAsync(int userId);

    // Url = the provider's hosted page to send the user to
    Task<(bool Success, string Message, string? Url)> StartCheckoutAsync(int userId, bool android);
    Task<(bool Success, string Message, string? Url)> CreatePortalAsync(int userId, bool android);

    // Cancel = stop renewing, Premium stays until the period end. Both are harmless when repeated.
    Task<(bool Success, string Message)> CancelAsync(int userId);
    Task<(bool Success, string Message)> ResumeAsync(int userId);

    // Asks the provider for the current state (after returning from checkout, or when a webhook was missed)
    Task<SubscriptionStatusDto> SyncAsync(int userId);

    // False when the notification isn't authentic. Throws when processing fails, so the provider retries.
    Task<bool> HandleWebhookAsync(string payload, string? signature);

    // Expiry safety net for subscriptions whose "ended" notification never arrived; returns how many ended
    Task<int> ExpireLapsedAsync(CancellationToken cancellationToken);

    // Development only
    Task<(bool Success, string Message)> SimulateAsync(int userId, FakeSubscriptionEvent simulated);
}
