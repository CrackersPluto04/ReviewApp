using System.Net;
using System.Net.Http.Json;
using System.Text;
using ReviewApp.Api.Tests.Support;

namespace ReviewApp.Api.Tests.Http;

// The real limits (multiplier 1). Every test gets its own host: the counters live in memory, per host,
// and in the test server every anonymous request comes from the same (empty) address.
public sealed class RateLimitTests : IntegrationTest, IAsyncLifetime
{
    private readonly ApiFactory _host = new(permitMultiplier: 1);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private HttpClient Anonymous() => Track(_host.CreateBrowser());

    private HttpClient As(DAL.Entities.User user) => Track(_host.CreateClientFor(user));

    // Sends the request `allowed` times (none may be limited), then once more, which must be
    private static async Task ShouldLimitAfterAsync(int allowed, Func<Task<HttpResponseMessage>> send)
    {
        for (var i = 0; i < allowed; i++)
            (await send()).StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests, $"request {i + 1} of {allowed}");

        var limited = await send();
        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await limited.Content.ReadFromJsonAsync<Dictionary<string, string>>())!["error"].ShouldBe("Too many requests. Please try again later.");
    }

    [Fact(Skip = "Known issue F4 (FINDINGS.md): the sliding window leases carry no RetryAfter metadata, so the header is never sent")]
    public async Task Rejection_SaysWhenToRetry()
    {
        var client = Anonymous();
        for (var i = 0; i < 10; i++)
            await client.PostAsJsonAsync("api/Auth/login", WrongLogin);

        (await client.PostAsJsonAsync("api/Auth/login", WrongLogin)).Headers.RetryAfter.ShouldNotBeNull();
    }

    private static object WrongLogin => new { email = "nobody@test.local", password = "Wrong-pass1" };

    [Fact]
    public async Task AuthAttempt_TenPerTenMinutes()
    {
        var client = Anonymous();

        await ShouldLimitAfterAsync(10, () => client.PostAsJsonAsync("api/Auth/login", WrongLogin));
    }

    [Fact]
    public async Task AuthAttempt_IsSharedByLoginAndRegister()
    {
        var client = Anonymous();
        for (var i = 0; i < 10; i++)
            await client.PostAsJsonAsync("api/Auth/login", WrongLogin);

        var register = await client.PostAsJsonAsync("api/Auth/register",
            new { username = TestData.Unique(), email = $"{TestData.Unique()}@test.local", password = "Passw0rd!" });

        register.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task EmailLink_HasItsOwnCounter_SoOpeningLinksNeverBlocksLogin()
    {
        var client = Anonymous();

        await ShouldLimitAfterAsync(10, () => client.PostAsJsonAsync("api/Security/verify-email", new { token = "x" }));

        (await client.PostAsJsonAsync("api/Auth/login", WrongLogin)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CredentialChange_FivePerUser()
    {
        var user = await Data.UserAsync();
        var other = await Data.UserAsync();
        var client = As(user);

        await ShouldLimitAfterAsync(5, () => client.PutAsJsonAsync("api/User/me/password", new { currentPassword = "Wrong-pass1", newPassword = "Whatever1" }));

        // Per account, not shared with other users
        (await As(other).PutAsJsonAsync("api/User/me/password", new { currentPassword = "Wrong-pass1", newPassword = "Whatever1" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task EmailSend_FivePerUser()
    {
        var user = await Data.UserAsync(u => u.IsEmailConfirmed = false);
        var client = As(user);

        await ShouldLimitAfterAsync(5, () => client.PostAsync("api/Security/resend-verification", null));
    }

    [Fact]
    public async Task ExternalApi_SixtyPerMinute()
    {
        var client = Anonymous();

        await ShouldLimitAfterAsync(60, () => client.GetAsync("api/Media/search/movie?query=alien"));
    }

    [Fact]
    public async Task Billing_FifteenPerTenMinutes()
    {
        var user = await Data.UserAsync();
        var client = As(user);

        await ShouldLimitAfterAsync(15, () => client.PostAsync("api/Subscription/sync", null));
    }

    [Fact]
    public async Task Global_ThreeHundredPerMinute()
    {
        var client = Anonymous();

        await ShouldLimitAfterAsync(300, () => client.GetAsync("api/Subscription/plan"));
    }

    [Fact]
    public async Task Webhook_IsNeverLimited()
    {
        var client = Anonymous();

        for (var i = 0; i < 320; i++)
        {
            var response = await client.PostAsync("api/Subscription/webhook", new StringContent("{}", Encoding.UTF8, "application/json"));
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, $"request {i + 1}");
        }
    }
}
