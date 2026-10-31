using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Tests.Support;

namespace ReviewApp.Api.Tests.Http;

public class SubscriptionEndpointTests : HttpTest
{
    private static HttpContent Webhook(string payload) => new StringContent(payload, Encoding.UTF8, "application/json");

    private Task<HttpResponseMessage> PostWebhookAsync(string payload, string? signature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "api/Subscription/webhook") { Content = Webhook(payload) };
        if (signature != null)
            request.Headers.Add("Stripe-Signature", signature);
        return Anonymous().SendAsync(request);
    }

    [Fact]
    public async Task Webhook_WithoutOrWithBadSignature_Is400()
    {
        var payload = TestPaymentProvider.WebhookPayload(TestData.Unique("evt_"), "invoice.paid");

        (await ErrorAsync(await PostWebhookAsync(payload, null), HttpStatusCode.BadRequest)).ShouldBe("Invalid signature.");
        (await ErrorAsync(await PostWebhookAsync(payload, "t=1,v1=forged"), HttpStatusCode.BadRequest)).ShouldBe("Invalid signature.");
    }

    [Fact]
    public async Task Webhook_SignedButIrrelevantType_IsAcknowledged()
    {
        var payload = TestPaymentProvider.WebhookPayload(TestData.Unique("evt_"), "customer.created");

        (await PostWebhookAsync(payload, TestPaymentProvider.ValidSignature)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Webhook_SignedEvent_UpdatesTheSubscription()
    {
        var user = await Data.UserAsync();
        var client = As(user);
        await ExpectAsync(await client.PostAsJsonAsync("api/Subscription/checkout", new { platform = "web" }), HttpStatusCode.OK);
        var row = await Query().UserSubscriptions.SingleAsync(s => s.UserID == user.ID);
        var payload = TestPaymentProvider.WebhookPayload(TestData.Unique("evt_"), "checkout.session.completed", row.ProviderCustomerID);

        (await PostWebhookAsync(payload, TestPaymentProvider.ValidSignature)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await ExpectAsync(await client.GetAsync("api/Subscription/status"), HttpStatusCode.OK)).Bool("isPremium").ShouldBeTrue();
    }

    [Fact]
    public async Task Status_NeverLeaksProviderIds()
    {
        var user = await Data.UserAsync();
        await Data.PremiumAsync(user);

        var response = await As(user).GetAsync("api/Subscription/status");
        var json = await response.Content.ReadAsStringAsync();

        await ExpectAsync(response, HttpStatusCode.OK);
        json.ShouldNotContain("cus_");
        json.ShouldNotContain("sub_");
        json.ShouldNotContain("provider", Case.Insensitive);
    }

    [Fact]
    public async Task Checkout_ReturnsTheProviderUrlAndValidatesThePlatform()
    {
        var user = await Data.UserAsync();
        var client = As(user);

        (await ExpectAsync(await client.PostAsJsonAsync("api/Subscription/checkout", new { platform = "ios" }), HttpStatusCode.BadRequest))
            .GetProperty("errors").TryGetProperty("Platform", out _).ShouldBeTrue();
        (await ExpectAsync(await client.PostAsJsonAsync("api/Subscription/checkout", new { platform = "web" }), HttpStatusCode.OK))
            .Str("url").ShouldBe($"{ApiFactory.FrontendBaseUrl}/subscription/return?status=success");
        (await ErrorAsync(await client.PostAsync("api/Subscription/cancel", null), HttpStatusCode.BadRequest))
            .ShouldBe("You don't have an active subscription.");
    }

    [Fact]
    public async Task Plan_IsPublic()
    {
        var body = await ExpectAsync(await Anonymous().GetAsync("api/Subscription/plan"), HttpStatusCode.OK);

        body.Str("currency").ShouldBe("EUR");
        body.GetProperty("features").GetArrayLength().ShouldBeGreaterThan(0);
    }

    [Theory]
    [InlineData("success", "reviewapp://subscription/success", "Thanks for subscribing!")]
    [InlineData("portal", "reviewapp://subscription/portal", "Done with your payment settings")]
    [InlineData("javascript:alert(1)", "reviewapp://subscription/cancelled", "Checkout cancelled")]
    public async Task AndroidReturnPage_OnlyLinksToKnownResults(string result, string link, string title)
    {
        var response = await Anonymous().GetAsync($"api/Subscription/return/android?result={Uri.EscapeDataString(result)}");
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
        response.Headers.GetValues("Content-Security-Policy").Single().ShouldStartWith("default-src 'none'");
        html.ShouldContain($"href=\"{link}\"");
        html.ShouldContain(title);
        html.ShouldNotContain("javascript:");
    }

    [Fact]
    public async Task DevSimulate_IsNotAvailableOutsideDevelopment()
    {
        var user = await Data.UserAsync();

        (await As(user).PostAsJsonAsync("api/Subscription/dev/simulate", new { @event = "Renewed" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SubscriptionActions_NeedLogin()
    {
        var anonymous = Anonymous();

        foreach (var url in new[] { "api/Subscription/checkout", "api/Subscription/portal", "api/Subscription/cancel", "api/Subscription/resume", "api/Subscription/sync" })
            (await anonymous.PostAsJsonAsync(url, new { platform = "web" })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, url);
        (await anonymous.GetAsync("api/Subscription/status")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CheckAuth_PicksUpANewSubscriptionWithoutLoggingInAgain()
    {
        var user = await Data.UserAsync();
        var client = As(user);
        (await ExpectAsync(await client.GetAsync("api/Auth/check-auth"), HttpStatusCode.OK)).Bool("isPremium").ShouldBeFalse();

        await ExpectAsync(await client.PostAsJsonAsync("api/Subscription/checkout", new { platform = "web" }), HttpStatusCode.OK);
        await ExpectAsync(await client.PostAsync("api/Subscription/sync", null), HttpStatusCode.OK);

        (await ExpectAsync(await client.GetAsync("api/Auth/check-auth"), HttpStatusCode.OK)).Bool("isPremium").ShouldBeTrue();
        (await Query().UserSubscriptions.SingleAsync(s => s.UserID == user.ID)).Status.ShouldBe(SubscriptionStatus.Active);
    }
}
