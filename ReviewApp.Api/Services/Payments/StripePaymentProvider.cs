using Microsoft.Extensions.Options;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services.Interfaces;
using Stripe;

namespace ReviewApp.Api.Services.Payments;

public class StripePaymentProvider : IPaymentProvider
{
    private readonly StripeClient _client;
    private readonly StripeOptions _options;

    public StripePaymentProvider(StripeClient client, IOptions<StripeOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public SubscriptionProvider Kind => SubscriptionProvider.Stripe;

    public Task<string> CreateCustomerAsync(int userId, string email, string username) =>
        Call(async () =>
        {
            var customer = await _client.V1.Customers.CreateAsync(new CustomerCreateOptions
            {
                Email = email,
                Name = username,
                Metadata = new Dictionary<string, string> { ["userId"] = userId.ToString() }
            });
            return customer.Id;
        });

    public Task<string> CreateCheckoutUrlAsync(int userId, string customerId, string successUrl, string cancelUrl) =>
        Call(async () =>
        {
            var session = await _client.V1.Checkout.Sessions.CreateAsync(new Stripe.Checkout.SessionCreateOptions
            {
                Mode = "subscription",
                Customer = customerId,
                ClientReferenceId = userId.ToString(),
                LineItems = [new Stripe.Checkout.SessionLineItemOptions { Price = _options.PriceId, Quantity = 1 }],
                SubscriptionData = new Stripe.Checkout.SessionSubscriptionDataOptions
                {
                    Metadata = new Dictionary<string, string> { ["userId"] = userId.ToString() }
                },
                SuccessUrl = successUrl,
                CancelUrl = cancelUrl
            });
            return session.Url;
        });

    public Task<string> CreatePortalUrlAsync(string customerId, string returnUrl) =>
        Call(async () =>
        {
            var session = await _client.V1.BillingPortal.Sessions.CreateAsync(new Stripe.BillingPortal.SessionCreateOptions
            {
                Customer = customerId,
                ReturnUrl = returnUrl
            });
            return session.Url;
        });

    public Task<ProviderSubscriptionSnapshot?> GetSubscriptionAsync(string subscriptionId) =>
        Call(async () =>
        {
            try
            {
                return (ProviderSubscriptionSnapshot?)ToSnapshot(await _client.V1.Subscriptions.GetAsync(subscriptionId));
            }
            catch (StripeException ex) when (ex.StripeError?.Code == "resource_missing")
            {
                return null;
            }
        });

    public Task<ProviderSubscriptionSnapshot?> FindLatestSubscriptionAsync(string customerId) =>
        Call(async () =>
        {
            var list = await _client.V1.Subscriptions.ListAsync(new SubscriptionListOptions
            {
                Customer = customerId,
                Status = "all",
                Limit = 10
            });

            // Newest first; a running one wins over a newer abandoned/ended one
            var snapshots = list.Data.OrderByDescending(s => s.Created).Select(ToSnapshot).ToList();
            return snapshots.FirstOrDefault(s => s.Status is SubscriptionStatus.Active or SubscriptionStatus.PastDue)
                ?? snapshots.FirstOrDefault();
        });

    public Task<ProviderSubscriptionSnapshot> SetCancelAtPeriodEndAsync(string subscriptionId, bool cancel) =>
        Call(async () => ToSnapshot(await _client.V1.Subscriptions.UpdateAsync(subscriptionId,
            new SubscriptionUpdateOptions { CancelAtPeriodEnd = cancel })));

    public Task CancelImmediatelyAsync(string subscriptionId) =>
        Call(async () => await _client.V1.Subscriptions.CancelAsync(subscriptionId));

    public PaymentWebhookEvent? ParseWebhook(string payload, string? signature)
    {
        if (string.IsNullOrEmpty(signature))
            return null;

        Event stripeEvent;
        try
        {
            // The endpoint's API version may differ from the SDK's. That's fine: only IDs are read from the payload,
            // the current state is always fetched through the API.
            stripeEvent = EventUtility.ConstructEvent(payload, signature, _options.WebhookSecret, throwOnApiVersionMismatch: false);
        }
        catch (StripeException)
        {
            return null;
        }

        var (customerId, subscriptionId) = stripeEvent.Data.Object switch
        {
            Stripe.Checkout.Session session => (session.CustomerId, session.SubscriptionId),
            Subscription subscription => (subscription.CustomerId, subscription.Id),
            Invoice invoice => (invoice.CustomerId, invoice.Parent?.SubscriptionDetails?.SubscriptionId),
            _ => (null, null)
        };

        return new PaymentWebhookEvent(stripeEvent.Id, stripeEvent.Type, customerId, subscriptionId);
    }

    private static ProviderSubscriptionSnapshot ToSnapshot(Subscription subscription)
    {
        // The billing period lives on the items (one item here: the Premium price)
        var periodEnd = subscription.Items?.Data.Count > 0
            ? subscription.Items.Data.Max(i => i.CurrentPeriodEnd)
            : (DateTime?)null;

        return new ProviderSubscriptionSnapshot(
            subscription.CustomerId,
            subscription.Id,
            SubscriptionRules.MapStripeStatus(subscription.Status),
            subscription.CancelAtPeriodEnd,
            periodEnd is { } end ? DateTime.SpecifyKind(end, DateTimeKind.Utc) : null);
    }

    private static async Task<T> Call<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (StripeException ex)
        {
            // The message goes to the log only, never to the client
            throw new PaymentProviderException($"Stripe request failed ({ex.StripeError?.Type}/{ex.StripeError?.Code}).", ex);
        }
    }

    private static Task Call(Func<Task> action) => Call(async () => { await action(); return true; });
}
