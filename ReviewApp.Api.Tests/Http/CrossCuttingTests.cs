using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services;
using ReviewApp.Api.Tests.Support;

namespace ReviewApp.Api.Tests.Http;

// Response side effects every client relies on: the achievement header, CORS and the error formats
public class CrossCuttingTests : HttpTest
{
    private static object ReviewRequest(string externalId, MediaType type = MediaType.Movie, decimal score = 8) => new
    {
        mediaDto = new { id = externalId + type, externalApiID = externalId, mediaType = type },
        reviewDto = new { score, reviewText = "Good", visibilityLevel = VisibilityLevel.Public }
    };

    [Fact]
    public async Task UnlockingAnAchievement_AddsTheHeaderForTheRequester()
    {
        var user = await Data.UserAsync();

        var response = await As(user).PostAsJsonAsync("api/Review", ReviewRequest(TestData.Unique("8")));

        await ExpectAsync(response, HttpStatusCode.OK);
        var unlocked = JsonDocument.Parse(response.Headers.GetValues(ResponseHeaderUnlockHandler.HeaderName).Single()).RootElement;
        unlocked.EnumerateArray().Select(a => (a.Str("groupCode"), a.Str("tier"))).ShouldContain(("MOVIE_MASTER", "Bronze"));
    }

    [Fact]
    public async Task NothingUnlocked_MeansNoHeader()
    {
        var user = await Data.UserAsync();
        var client = As(user);
        await client.PostAsJsonAsync("api/Review", ReviewRequest(TestData.Unique("8")));

        var second = await client.PostAsJsonAsync("api/Review", ReviewRequest(TestData.Unique("8")));

        await ExpectAsync(second, HttpStatusCode.OK);
        second.Headers.Contains(ResponseHeaderUnlockHandler.HeaderName).ShouldBeFalse();
    }

    [Fact]
    public async Task Cors_AllowsTheFrontendWithCredentialsAndExposesTheUnlockHeader()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "api/Review");
        request.Headers.Add("Origin", ApiFactory.FrontendBaseUrl);
        request.Headers.Add("Access-Control-Request-Method", "POST");

        var preflight = await Anonymous().SendAsync(request);

        preflight.Headers.GetValues("Access-Control-Allow-Origin").Single().ShouldBe(ApiFactory.FrontendBaseUrl);
        preflight.Headers.GetValues("Access-Control-Allow-Credentials").Single().ShouldBe("true");

        var actual = new HttpRequestMessage(HttpMethod.Get, "api/Subscription/plan");
        actual.Headers.Add("Origin", ApiFactory.FrontendBaseUrl);
        var response = await Anonymous().SendAsync(actual);
        response.Headers.GetValues("Access-Control-Expose-Headers").Single().ShouldContain(ResponseHeaderUnlockHandler.HeaderName);
    }

    [Fact]
    public async Task Cors_RejectsOtherOrigins()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "api/Subscription/plan");
        request.Headers.Add("Origin", "https://evil.test");

        var response = await Anonymous().SendAsync(request);

        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    [Fact]
    public async Task BusinessErrors_UseTheErrorShape_ValidationErrors_UseProblemDetails()
    {
        var user = await Data.UserAsync();
        var client = As(user);

        var business = await ExpectAsync(await client.PostAsJsonAsync("api/Follower/" + user.ID + "/follow", new { }), HttpStatusCode.BadRequest);
        var validation = await ExpectAsync(await client.PostAsJsonAsync("api/Collection", new { name = "" }), HttpStatusCode.BadRequest);
        var notFound = await ExpectAsync(await client.GetAsync("api/Review/-1"), HttpStatusCode.NotFound);

        business.Str("error").ShouldBe("You cannot follow yourself.");
        validation.GetProperty("errors").TryGetProperty("Name", out _).ShouldBeTrue();
        validation.Int("status").ShouldBe(400);
        notFound.Str("error").ShouldBe("Review not found.");
    }

    [Fact]
    public async Task UnknownRoute_Is404()
    {
        (await Anonymous().GetAsync("api/DoesNotExist")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
