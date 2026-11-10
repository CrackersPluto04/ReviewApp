using ReviewApp.Api.Enums;

namespace ReviewApp.Api.Services;

/// <summary>
/// Subscription settings, bound from the <c>Subscription</c> configuration section. The committed defaults use
/// the Fake provider, which <c>Program.cs</c> only allows in Development, so a real deployment must configure Stripe.
/// </summary>
public class SubscriptionOptions
{
    public const string SectionName = "Subscription";

    public SubscriptionProvider Provider { get; set; } = SubscriptionProvider.Fake;

    /// <summary>The displayed price only. The real price is the Stripe price (<c>Stripe:PriceId</c>); keep the two in sync.</summary>
    public decimal PriceAmount { get; set; } = 4.99m;
    public string Currency { get; set; } = "EUR";
    public string Interval { get; set; } = "month";

    /// <summary>
    /// Base URL of the API as the phone's browser reaches it, for the page that sends the user back to the Android
    /// app after checkout. Plain HTTP locally (the browser does not trust the dev certificate), HTTPS when deployed.
    /// </summary>
    public string MobileReturnBaseUrl { get; set; } = "http://localhost:5062";
}

/// <summary>
/// Stripe settings, bound from the <c>Stripe</c> configuration section. Secrets only go into the gitignored
/// <c>appsettings.Development.json</c>.
/// </summary>
public class StripeOptions
{
    public const string SectionName = "Stripe";

    public string SecretKey { get; set; } = string.Empty;
    /// <summary>The signing secret that webhook signatures are verified with.</summary>
    public string WebhookSecret { get; set; } = string.Empty;
    /// <summary>The ID of the monthly Premium price.</summary>
    public string PriceId { get; set; } = string.Empty;
}
