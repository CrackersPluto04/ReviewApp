using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using ReviewApp.Api.Services;
using ReviewApp.Api.Services.Payments;
using Stripe;

namespace ReviewApp.Api.Tests.Unit;

// Signature verification and ID extraction of Stripe webhooks; no request leaves the process
public class StripeWebhookParsingTests
{
    private const string Secret = "whsec_test_secret";

    private readonly StripePaymentProvider _provider = new(new StripeClient("sk_test_dummy"),
        Options.Create(new StripeOptions { WebhookSecret = Secret, SecretKey = "sk_test_dummy", PriceId = "price_1" }));

    // Stripe's scheme: HMAC-SHA256 of "{timestamp}.{payload}" with the endpoint secret
    private static string Sign(string payload, string secret = Secret, DateTimeOffset? at = null)
    {
        var timestamp = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));
        return $"t={timestamp},v1={Convert.ToHexString(signature).ToLowerInvariant()}";
    }

    private static string Event(string type, string dataObject) =>
        $$"""{ "id": "evt_1", "object": "event", "api_version": "2020-08-27", "type": "{{type}}", "data": { "object": {{dataObject}} } }""";

    [Fact]
    public void SubscriptionEvent_YieldsCustomerAndSubscription()
    {
        var payload = Event("customer.subscription.updated", """{ "id": "sub_1", "object": "subscription", "customer": "cus_1" }""");

        var parsed = _provider.ParseWebhook(payload, Sign(payload));

        parsed.ShouldNotBeNull();
        parsed.EventID.ShouldBe("evt_1");
        parsed.Type.ShouldBe("customer.subscription.updated");
        parsed.CustomerID.ShouldBe("cus_1");
        parsed.SubscriptionID.ShouldBe("sub_1");
    }

    [Fact]
    public void CheckoutSession_YieldsItsSubscription()
    {
        var payload = Event("checkout.session.completed",
            """{ "id": "cs_1", "object": "checkout.session", "customer": "cus_2", "subscription": "sub_2" }""");

        var parsed = _provider.ParseWebhook(payload, Sign(payload));

        parsed.ShouldNotBeNull();
        parsed.CustomerID.ShouldBe("cus_2");
        parsed.SubscriptionID.ShouldBe("sub_2");
    }

    [Fact]
    public void Invoice_YieldsTheSubscriptionFromItsParent()
    {
        var payload = Event("invoice.payment_failed",
            """{ "id": "in_1", "object": "invoice", "customer": "cus_3", "parent": { "type": "subscription_details", "subscription_details": { "subscription": "sub_3" } } }""");

        var parsed = _provider.ParseWebhook(payload, Sign(payload));

        parsed.ShouldNotBeNull();
        parsed.CustomerID.ShouldBe("cus_3");
        parsed.SubscriptionID.ShouldBe("sub_3");
    }

    [Fact]
    public void UnrelatedObject_HasNoIds()
    {
        var payload = Event("customer.created", """{ "id": "cus_4", "object": "customer" }""");

        var parsed = _provider.ParseWebhook(payload, Sign(payload));

        parsed.ShouldNotBeNull();
        parsed.CustomerID.ShouldBeNull();
        parsed.SubscriptionID.ShouldBeNull();
    }

    [Fact]
    public void MissingSignature_IsRejected()
    {
        var payload = Event("invoice.paid", """{ "id": "in_1", "object": "invoice" }""");

        _provider.ParseWebhook(payload, null).ShouldBeNull();
        _provider.ParseWebhook(payload, string.Empty).ShouldBeNull();
    }

    [Fact]
    public void WrongSecretOrTamperedPayload_IsRejected()
    {
        var payload = Event("invoice.paid", """{ "id": "in_1", "object": "invoice", "customer": "cus_1" }""");

        _provider.ParseWebhook(payload, Sign(payload, "whsec_other")).ShouldBeNull();
        _provider.ParseWebhook(payload.Replace("cus_1", "cus_2"), Sign(payload)).ShouldBeNull();
        _provider.ParseWebhook(payload, "t=1,v1=deadbeef").ShouldBeNull();
    }

    [Fact]
    public void ReplayedOldSignature_IsRejected()
    {
        var payload = Event("invoice.paid", """{ "id": "in_1", "object": "invoice" }""");

        // Outside Stripe's 5 minute tolerance
        _provider.ParseWebhook(payload, Sign(payload, at: DateTimeOffset.UtcNow.AddMinutes(-10))).ShouldBeNull();
    }
}
