using ReviewApp.Api.Enums;

namespace ReviewApp.Api.Services.Interfaces;

// A verified provider notification, reduced to what is needed to look the subscription up again.
public record PaymentWebhookEvent(string EventID, string Type, string? CustomerID, string? SubscriptionID);

// The payment provider behind the subscription (Stripe, or the Fake one in development).
// The app never handles card data: payment and payment details happen on the provider's hosted pages.
// Methods throw PaymentProviderException when the provider can't be reached or refuses the request.
public interface IPaymentProvider
{
    SubscriptionProvider Kind { get; }

    Task<string> CreateCustomerAsync(int userId, string email, string username);
    Task<string> CreateCheckoutUrlAsync(int userId, string customerId, string successUrl, string cancelUrl);
    Task<string> CreatePortalUrlAsync(string customerId, string returnUrl);

    Task<ProviderSubscriptionSnapshot?> GetSubscriptionAsync(string subscriptionId);
    // The customer's running subscription if there is one, otherwise the newest one (null if none)
    Task<ProviderSubscriptionSnapshot?> FindLatestSubscriptionAsync(string customerId);

    Task<ProviderSubscriptionSnapshot> SetCancelAtPeriodEndAsync(string subscriptionId, bool cancel);
    // Only for an accidental second subscription of the same user
    Task CancelImmediatelyAsync(string subscriptionId);

    // Null when the signature is missing or invalid
    PaymentWebhookEvent? ParseWebhook(string payload, string? signature);
}

public class PaymentProviderException : Exception
{
    public PaymentProviderException(string message, Exception? inner = null) : base(message, inner) { }
}
