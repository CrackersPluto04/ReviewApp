using System.Collections.Concurrent;
using System.Text.Json;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services;
using ReviewApp.Api.Services.Interfaces;
using ReviewApp.Api.Services.Payments;

namespace ReviewApp.Api.Tests.Support;

// The FakePaymentProvider behaviour (checkout activates at once) plus what the tests need on top:
// webhooks that parse when signed with ValidSignature, setting the provider-side state directly,
// and an outage for chosen users / customers. Everything is keyed per user, so parallel tests don't interfere.
public class TestPaymentProvider : IPaymentProvider
{
    public const string ValidSignature = "test-signature";

    private readonly FakePaymentProvider _inner;
    private readonly ConcurrentDictionary<string, byte> _unavailable = new();

    public TestPaymentProvider(IServiceScopeFactory scopeFactory)
    {
        _inner = new FakePaymentProvider(scopeFactory);
    }

    public SubscriptionProvider Kind => SubscriptionProvider.Fake;

    // The provider "goes down" for this user's requests (by user ID before a customer exists, by customer ID after)
    public void MakeUnavailable(int userId) => _unavailable[$"user:{userId}"] = 0;
    public void MakeUnavailable(string customerId) => _unavailable[$"cus:{customerId}"] = 0;

    // What the provider reports from now on, e.g. a renewal or a failed payment that a webhook announces
    public Task<ProviderSubscriptionSnapshot> SimulateAsync(string subscriptionId, FakeSubscriptionEvent simulated) =>
        _inner.SimulateAsync(subscriptionId, simulated);

    public static string WebhookPayload(string eventId, string type, string? customerId = null, string? subscriptionId = null) =>
        JsonSerializer.Serialize(new { id = eventId, type, customer = customerId, subscription = subscriptionId });

    public Task<string> CreateCustomerAsync(int userId, string email, string username)
    {
        ThrowIfUnavailable($"user:{userId}");
        return _inner.CreateCustomerAsync(userId, email, username);
    }

    public Task<string> CreateCheckoutUrlAsync(int userId, string customerId, string successUrl, string cancelUrl)
    {
        ThrowIfUnavailable($"cus:{customerId}");
        return _inner.CreateCheckoutUrlAsync(userId, customerId, successUrl, cancelUrl);
    }

    public Task<string> CreatePortalUrlAsync(string customerId, string returnUrl)
    {
        ThrowIfUnavailable($"cus:{customerId}");
        return _inner.CreatePortalUrlAsync(customerId, returnUrl);
    }

    public Task<ProviderSubscriptionSnapshot?> GetSubscriptionAsync(string subscriptionId) => _inner.GetSubscriptionAsync(subscriptionId);

    public Task<ProviderSubscriptionSnapshot?> FindLatestSubscriptionAsync(string customerId)
    {
        ThrowIfUnavailable($"cus:{customerId}");
        return _inner.FindLatestSubscriptionAsync(customerId);
    }

    public Task<ProviderSubscriptionSnapshot> SetCancelAtPeriodEndAsync(string subscriptionId, bool cancel) =>
        _inner.SetCancelAtPeriodEndAsync(subscriptionId, cancel);

    public Task CancelImmediatelyAsync(string subscriptionId) => _inner.CancelImmediatelyAsync(subscriptionId);

    public PaymentWebhookEvent? ParseWebhook(string payload, string? signature)
    {
        if (signature != ValidSignature) return null;

        using var json = JsonDocument.Parse(payload);
        var root = json.RootElement;
        return new PaymentWebhookEvent(
            root.GetProperty("id").GetString()!,
            root.GetProperty("type").GetString()!,
            root.TryGetProperty("customer", out var customer) ? customer.GetString() : null,
            root.TryGetProperty("subscription", out var subscription) ? subscription.GetString() : null);
    }

    private void ThrowIfUnavailable(string key)
    {
        if (_unavailable.ContainsKey(key))
            throw new PaymentProviderException("The test provider is unavailable.");
    }
}
