using System.Net;
using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Filters;
using ReviewApp.Api.Services;
using ReviewApp.Api.Tests.Support;

namespace ReviewApp.Api.Tests.Http;

// api/Recognition through the whole pipeline: auth, the Premium lock, the multipart upload, status codes and shapes
public class RecognitionEndpointTests : HttpTest
{
    private const string Url = "api/Recognition";

    private static string Tag() => TestData.Unique("img");

    private async Task<User> PremiumUserAsync()
    {
        var user = await Data.UserAsync();
        await Data.PremiumAsync(user);
        return user;
    }

    [Fact]
    public async Task Anonymous_Gets401Everywhere()
    {
        var client = Anonymous();

        (await client.PostAsync(Url, Photo(TestImages.Jpeg(Tag())))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.GetAsync($"{Url}/abc/items")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.GetAsync($"{Url}/allowance")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task FreeUser_GetsTheUpsell_ButCanReadWhetherTheFeatureExists()
    {
        var user = await Data.UserAsync();
        var tag = Tag();
        var client = As(user);

        var body = await ExpectAsync(await client.PostAsync(Url, Photo(TestImages.Jpeg(tag))), HttpStatusCode.PaymentRequired);
        body.Str("code").ShouldBe(RequiresFeatureAttribute.PremiumRequiredCode);
        body.Str("feature").ShouldBe(FeatureKeys.CameraRecognition);
        (await client.GetAsync($"{Url}/abc/items")).StatusCode.ShouldBe(HttpStatusCode.PaymentRequired);
        App.Recognition.Calls(tag).ShouldBe(0);

        var allowance = await ExpectAsync(await client.GetAsync($"{Url}/allowance"), HttpStatusCode.OK);
        allowance.Bool("enabled").ShouldBeTrue();
        allowance.Bool("premium").ShouldBeFalse();
        allowance.GetProperty("remaining").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task PremiumUser_RecognisesAndPagesThroughTheList()
    {
        var user = await PremiumUserAsync();
        var client = As(user);

        var first = await ExpectAsync(await client.PostAsync(Url, Photo(TestImages.Jpeg(Tag()))), HttpStatusCode.OK);

        first.Str("outcome").ShouldBe("recognised");
        first.Int("total").ShouldBe(12);
        first.Bool("hasMore").ShouldBeTrue();
        var items = first.GetProperty("items");
        items.GetArrayLength().ShouldBe(10);
        items[0].Str("tier").ShouldBe("direct");
        var media = items[0].GetProperty("media");
        media.Int("mediaType").ShouldBe((int)MediaType.Movie);
        media.Str("externalApiID").ShouldNotBeNullOrEmpty();
        first.GetProperty("allowance").Int("remaining").ShouldBe(19);

        var id = first.Str("recognitionId");
        var more = await ExpectAsync(await client.GetAsync($"{Url}/{id}/items?offset={first.Int("nextOffset")}"), HttpStatusCode.OK);
        more.GetProperty("items").GetArrayLength().ShouldBe(2);
        more.Bool("hasMore").ShouldBeFalse();

        (await ExpectAsync(await client.GetAsync($"{Url}/allowance"), HttpStatusCode.OK)).Int("remaining").ShouldBe(19);
    }

    [Fact]
    public async Task PngIsAcceptedToo()
    {
        var user = await PremiumUserAsync();

        await ExpectAsync(await As(user).PostAsync(Url, Photo(TestImages.Png(), contentType: "image/png")), HttpStatusCode.OK);
    }

    [Fact]
    public async Task SomeoneElsesOrAnUnknownRecognition_IsNotFound()
    {
        var owner = await PremiumUserAsync();
        var other = await PremiumUserAsync();
        var id = (await ExpectAsync(await As(owner).PostAsync(Url, Photo(TestImages.Jpeg(Tag()))), HttpStatusCode.OK)).Str("recognitionId");

        (await ExpectAsync(await As(other).GetAsync($"{Url}/{id}/items?offset=10"), HttpStatusCode.NotFound)).Str("code").ShouldBe("not_found");
        (await ExpectAsync(await As(owner).GetAsync($"{Url}/unknown/items"), HttpStatusCode.NotFound)).Str("code").ShouldBe("not_found");
    }

    [Fact]
    public async Task LapsedSubscription_StopsShowMore()
    {
        var user = await PremiumUserAsync();
        var client = As(user);
        var id = (await ExpectAsync(await client.PostAsync(Url, Photo(TestImages.Jpeg(Tag()))), HttpStatusCode.OK)).Str("recognitionId");

        await Query().UserSubscriptions.Where(s => s.UserID == user.ID)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, SubscriptionStatus.Ended));

        (await client.GetAsync($"{Url}/{id}/items?offset=10")).StatusCode.ShouldBe(HttpStatusCode.PaymentRequired);
    }

    [Fact]
    public async Task BadUploads_AreRefusedWithClearMessages()
    {
        var user = await PremiumUserAsync();
        var client = As(user);

        (await ErrorAsync(await client.PostAsync(Url, Photo(TestImages.Heic(), contentType: "image/jpeg")), HttpStatusCode.BadRequest))
            .ShouldBe("Only JPEG and PNG photos are supported.");
        (await ErrorAsync(await client.PostAsync(Url, Photo(TestImages.Jpeg(Tag()), name: "file")), HttpStatusCode.BadRequest))
            .ShouldContain("No photo was sent");
        (await ErrorAsync(await PostJsonAsync(client, Url, new { image = "base64..." }), HttpStatusCode.BadRequest))
            .ShouldBe("Send the photo as multipart/form-data.");
        (await ErrorAsync(await client.PostAsync(Url, Photo(TestImages.Jpeg(Tag(), 20000, 20000))), HttpStatusCode.RequestEntityTooLarge))
            .ShouldBe("The photo's resolution is too high.");
        (await Query().RecognitionUsages.CountAsync(u => u.UserID == user.ID)).ShouldBe(0);
    }

    [Theory]
    [InlineData(5 * 1024 * 1024 + 1)] // just over: the streamed part is cut off
    [InlineData(7 * 1024 * 1024)]     // far over: refused by the declared length before reading
    public async Task OversizedPhoto_Is413(int size)
    {
        var user = await PremiumUserAsync();
        var image = new byte[size];
        TestImages.Jpeg(Tag()).CopyTo(image, 0);

        (await ErrorAsync(await As(user).PostAsync(Url, Photo(image)), HttpStatusCode.RequestEntityTooLarge)).ShouldContain("too large");
    }

    [Fact]
    public async Task DailyLimit_Is429_WithTheResetTime()
    {
        var user = await PremiumUserAsync();
        await Data.RecognitionUsageAsync(user, 20);

        var body = await ExpectAsync(await As(user).PostAsync(Url, Photo(TestImages.Jpeg(Tag()))), HttpStatusCode.TooManyRequests);

        body.Str("code").ShouldBe("daily_limit");
        body.GetProperty("resetsAt").GetDateTime().ShouldBe(DateTime.UtcNow.Date.AddDays(1));
    }

    [Fact]
    public async Task ParallelRecognition_Is409()
    {
        var user = await PremiumUserAsync();
        var tag = Tag();
        var release = new TaskCompletionSource();
        App.Recognition.Hold(tag, release.Task);
        var client = As(user);

        var running = client.PostAsync(Url, Photo(TestImages.Jpeg(tag)));
        await TestWait.UntilAsync(() => App.Recognition.Calls(tag) == 1);
        var second = await client.PostAsync(Url, Photo(TestImages.Jpeg(Tag())));
        release.SetResult();

        (await ExpectAsync(second, HttpStatusCode.Conflict)).Str("code").ShouldBe("busy");
        (await running).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ProviderOutage_Is503()
    {
        var user = await PremiumUserAsync();
        var tag = Tag();
        App.Recognition.Fail(tag);

        var body = await ExpectAsync(await As(user).PostAsync(Url, Photo(TestImages.Jpeg(tag))), HttpStatusCode.ServiceUnavailable);

        body.Str("code").ShouldBe("recognition_unavailable");
        body.Str("error")!.ShouldNotContain("Scripted");
    }

    [Fact]
    public async Task SwitchedOff_Is503_AndTheAllowanceSaysSo()
    {
        await using var host = new ApiFactory(settings: new Dictionary<string, string> { ["Recognition:Enabled"] = "false" });
        var user = await PremiumUserAsync();
        var client = Track(host.CreateClientFor(user));

        (await ExpectAsync(await client.PostAsync(Url, Photo(TestImages.Jpeg(Tag()))), HttpStatusCode.ServiceUnavailable))
            .Str("code").ShouldBe("recognition_unavailable");
        (await ExpectAsync(await client.GetAsync($"{Url}/allowance"), HttpStatusCode.OK)).Bool("enabled").ShouldBeFalse();
    }
}
