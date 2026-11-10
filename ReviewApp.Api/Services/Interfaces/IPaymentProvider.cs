using ReviewApp.Api.Enums;

namespace ReviewApp.Api.Services.Interfaces;

/// <summary>
/// A verified provider notification, reduced to what is needed to look the subscription up again. The event is
/// only a trigger: the current state is always fetched from the provider.
/// </summary>
/// <param name="EventID">The provider's event ID, stored to ignore redeliveries.</param>
/// <param name="Type">The provider's event type; types the app does not handle are acknowledged and ignored.</param>
/// <param name="CustomerID">The provider's customer, when the event names one.</param>
/// <param name="SubscriptionID">The provider's subscription, when the event names one.</param>
public record PaymentWebhookEvent(string EventID, string Type, string? CustomerID, string? SubscriptionID);

/// <summary>
/// The payment provider behind the subscription (Stripe, or the Fake one in development). The app never handles
/// card data: payment and payment details happen on the provider's hosted pages.
/// </summary>
/// <remarks>Methods throw <see cref="PaymentProviderException"/> when the provider cannot be reached or refuses the request.</remarks>
public interface IPaymentProvider
{
    /// <summary>Which provider this is.</summary>
    SubscriptionProvider Kind { get; }

    /// <summary>Creates the provider-side customer for a user.</summary>
    /// <returns>The provider's customer ID.</returns>
    Task<string> CreateCustomerAsync(int userId, string email, string username);

    /// <summary>Creates a hosted checkout page for the Premium plan.</summary>
    /// <param name="userId">The subscribing user, recorded with the provider so events can be matched back.</param>
    /// <param name="customerId">The provider's customer ID from <see cref="CreateCustomerAsync"/>.</param>
    /// <param name="successUrl">Where the provider sends the user after paying.</param>
    /// <param name="cancelUrl">Where the provider sends the user if they back out.</param>
    /// <returns>The page's URL.</returns>
    Task<string> CreateCheckoutUrlAsync(int userId, string customerId, string successUrl, string cancelUrl);

    /// <summary>Creates a customer portal session, where payment details and invoices are managed.</summary>
    /// <param name="customerId">The provider's customer ID.</param>
    /// <param name="returnUrl">Where the portal sends the user back to.</param>
    /// <returns>The page's URL.</returns>
    Task<string> CreatePortalUrlAsync(string customerId, string returnUrl);

    /// <summary>Reads a subscription's current state from the provider.</summary>
    /// <returns>The snapshot, or null if the provider does not know the subscription.</returns>
    Task<ProviderSubscriptionSnapshot?> GetSubscriptionAsync(string subscriptionId);

    /// <summary>Finds the customer's running subscription if there is one, otherwise the newest one.</summary>
    /// <returns>The snapshot, or null if the customer has none.</returns>
    Task<ProviderSubscriptionSnapshot?> FindLatestSubscriptionAsync(string customerId);

    /// <summary>Stops or resumes renewal while keeping the subscription until the period end.</summary>
    /// <param name="subscriptionId">The provider's subscription ID.</param>
    /// <param name="cancel">True to stop renewing, false to resume.</param>
    /// <returns>The subscription's state after the change.</returns>
    Task<ProviderSubscriptionSnapshot> SetCancelAtPeriodEndAsync(string subscriptionId, bool cancel);

    /// <summary>Ends a subscription at once. Only for an accidental second subscription of the same user.</summary>
    Task CancelImmediatelyAsync(string subscriptionId);

    /// <summary>Verifies a webhook's signature over the raw payload and extracts the event.</summary>
    /// <param name="payload">The exact request body; the signature covers its bytes.</param>
    /// <param name="signature">The signature header.</param>
    /// <returns>The event, or null when the signature is missing or invalid.</returns>
    PaymentWebhookEvent? ParseWebhook(string payload, string? signature);
}

/// <summary>
/// The payment provider could not be reached or refused a request.
/// </summary>
public class PaymentProviderException : Exception
{
    public PaymentProviderException(string message, Exception? inner = null) : base(message, inner) { }
}
