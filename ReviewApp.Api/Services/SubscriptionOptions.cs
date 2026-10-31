using ReviewApp.Api.Enums;

namespace ReviewApp.Api.Services;

// Bound from the "Subscription" configuration section. The committed defaults use the Fake provider,
// which Program.cs only allows in Development, so a real deployment must configure Stripe.
public class SubscriptionOptions
{
    public const string SectionName = "Subscription";

    public SubscriptionProvider Provider { get; set; } = SubscriptionProvider.Fake;

    // Displayed price only: the real one is the Stripe price (Stripe:PriceId), keep the two in sync
    public decimal PriceAmount { get; set; } = 4.99m;
    public string Currency { get; set; } = "EUR";
    public string Interval { get; set; } = "month";

    // Base URL of the API as the phone's browser reaches it, for the page that sends the user back to the
    // Android app after checkout. Plain HTTP locally (the browser doesn't trust the dev certificate), HTTPS when deployed.
    public string MobileReturnBaseUrl { get; set; } = "http://localhost:5062";
}

// Bound from the "Stripe" configuration section. Secrets only go into the gitignored appsettings.Development.json.
public class StripeOptions
{
    public const string SectionName = "Stripe";

    public string SecretKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public string PriceId { get; set; } = string.Empty;
}
