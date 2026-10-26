using ReviewApp.Api.Enums;
using ReviewApp.Api.Services.Interfaces;
using ReviewApp.Api.Services.Payments;

namespace ReviewApp.Api.Services;

public static class SubscriptionSetup
{
    // Registers the subscription services and the configured payment provider. Fails fast on a
    // configuration that would either not work (missing Stripe keys) or hand out Premium for free (Fake outside Development).
    public static void AddSubscriptions(this WebApplicationBuilder builder)
    {
        var subscriptionSection = builder.Configuration.GetSection(SubscriptionOptions.SectionName);
        var subscriptionOptions = subscriptionSection.Get<SubscriptionOptions>() ?? new SubscriptionOptions();
        builder.Services.Configure<SubscriptionOptions>(subscriptionSection);

        if (subscriptionOptions.Provider == SubscriptionProvider.Fake)
        {
            if (!builder.Environment.IsDevelopment())
                throw new InvalidOperationException("Subscription:Provider 'Fake' is only allowed in Development, configure Stripe.");

            // Singleton: it keeps the fake subscriptions in memory
            builder.Services.AddSingleton<IPaymentProvider, FakePaymentProvider>();
        }
        else
        {
            var stripeSection = builder.Configuration.GetSection(StripeOptions.SectionName);
            var stripeOptions = stripeSection.Get<StripeOptions>();
            if (string.IsNullOrEmpty(stripeOptions?.SecretKey) || string.IsNullOrEmpty(stripeOptions.WebhookSecret) || string.IsNullOrEmpty(stripeOptions.PriceId))
                throw new InvalidOperationException("Stripe:SecretKey, Stripe:WebhookSecret and Stripe:PriceId must be configured when Subscription:Provider is Stripe.");

            builder.Services.Configure<StripeOptions>(stripeSection);
            builder.Services.AddSingleton(new Stripe.StripeClient(stripeOptions.SecretKey));
            builder.Services.AddScoped<IPaymentProvider, StripePaymentProvider>();
        }

        builder.Services.AddScoped<ISubscriptionNotifier, SubscriptionNotifier>();
        builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();
        builder.Services.AddHostedService<SubscriptionExpiryService>();
    }

    // UseHttpsRedirection, except for the Android checkout return page in Development: it is opened in the phone's
    // browser, which doesn't trust the local dev certificate, so locally it is served over plain HTTP (port 5062).
    public static void UseHttpsRedirectionForApi(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            app.UseHttpsRedirection();
            return;
        }

        app.UseWhen(
            context => !context.Request.Path.StartsWithSegments("/api/Subscription/return"),
            branch => branch.UseHttpsRedirection());
    }
}
