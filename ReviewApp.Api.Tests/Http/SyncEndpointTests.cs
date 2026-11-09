using System.Net;
using System.Net.Http.Json;
using ReviewApp.Api.Services.Sync;
using ReviewApp.Api.Tests.Support;

namespace ReviewApp.Api.Tests.Http;

// POST api/Sync/push through the whole pipeline: auth, the request checks, and the JSON the Android app reads
public class SyncEndpointTests : HttpTest
{
    private const string Push = "api/Sync/push";

    private static object ProfileOp(Guid? opId = null, string bio = "Synced") =>
        new { opId = opId ?? Guid.NewGuid(), type = SyncOpTypes.ProfileUpdate, payload = new { fields = new { bio } } };

    [Fact]
    public async Task Anonymous_IsUnauthorized()
    {
        (await Anonymous().PostAsJsonAsync(Push, new { userId = 1, ops = Array.Empty<object>() }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task FreeUser_CanPush_AndGetsOneResultPerOp()
    {
        // Not Premium-gated: changes made offline still arrive after Premium lapses
        var user = await Data.UserAsync();
        var media = await Data.MediaAsync();
        var opId = Guid.NewGuid();

        var body = await ExpectAsync(await As(user).PostAsJsonAsync(Push, new
        {
            userId = user.ID,
            ops = new object[]
            {
                new
                {
                    opId,
                    type = SyncOpTypes.ReviewCreate,
                    clientRef = "r1",
                    payload = new { media = new { type = 0, externalApiId = media.ExternalApiID }, fields = new { score = 7.5, visibility = 1 } }
                },
                ProfileOp()
            }
        }), HttpStatusCode.OK);

        var results = body.GetProperty("results");
        results.GetArrayLength().ShouldBe(2);
        var created = results[0];
        created.GetProperty("opId").GetGuid().ShouldBe(opId);
        created.Str("status").ShouldBe("applied");
        created.TryGetProperty("reason", out _).ShouldBeFalse();
        var entity = created.GetProperty("entity");
        entity.Int("id").ShouldBe(created.Int("entityId"));
        entity.Dec("score").ShouldBe(7.5m);
        entity.GetProperty("version").GetInt64().ShouldBeGreaterThan(0);
        entity.Str("createdAt")!.ShouldEndWith("Z");
        body.GetProperty("unlocked").GetArrayLength().ShouldBeGreaterThan(0);
        body.TryGetProperty("serverTime", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task AnotherAccountsQueue_IsAConflict()
    {
        var user = await Data.UserAsync();

        var body = await ExpectAsync(await As(user).PostAsJsonAsync(Push, new { userId = user.ID + 1, ops = new[] { ProfileOp() } }),
            HttpStatusCode.Conflict);

        body.Str("code").ShouldBe("user_mismatch");
    }

    [Fact]
    public async Task TooManyOps_AreRefusedAsAWhole()
    {
        var user = await Data.UserAsync();
        var ops = Enumerable.Range(0, SyncService.MaxOps + 1).Select(_ => ProfileOp()).ToArray();

        var body = await ExpectAsync(await As(user).PostAsJsonAsync(Push, new { userId = user.ID, ops }), HttpStatusCode.BadRequest);

        body.Str("code").ShouldBe("too_many_ops");
    }

    [Fact]
    public async Task TheSameOpIdTwiceInOneRequest_IsRefused()
    {
        var user = await Data.UserAsync();
        var opId = Guid.NewGuid();

        var body = await ExpectAsync(await As(user).PostAsJsonAsync(Push, new { userId = user.ID, ops = new[] { ProfileOp(opId), ProfileOp(opId) } }),
            HttpStatusCode.BadRequest);

        body.Str("code").ShouldBe("duplicate_op");
    }

    [Fact]
    public async Task MalformedRequest_IsABadRequest()
    {
        var user = await Data.UserAsync();

        (await As(user).PostAsJsonAsync(Push, new { ops = new[] { ProfileOp() } })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await As(user).PostAsJsonAsync(Push, new { userId = user.ID, ops = new[] { new { type = SyncOpTypes.ProfileUpdate } } }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /* GET changes, POST visible */

    [Fact]
    public async Task Changes_Anonymous_IsUnauthorized()
    {
        (await Anonymous().GetAsync("api/Sync/changes")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Changes_FirstSync_HasTheOwnContentAndTheAccount()
    {
        var user = await Data.UserAsync();
        var review = await Data.ReviewAsync(user, visibility: Enums.VisibilityLevel.Private);
        await AllWritesCommittedAsync();

        var body = await ExpectAsync(await As(user).GetAsync("api/Sync/changes?since=0"), HttpStatusCode.OK);

        body.Bool("fullResync").ShouldBeTrue();
        body.GetProperty("reviews").EnumerateArray().Select(r => r.Int("id")).ShouldContain(review.ID);
        body.GetProperty("collections").EnumerateArray().ShouldContain(c => c.Bool("isDefault"));
        body.GetProperty("cursor").GetInt64().ShouldBeGreaterThan(0);
        body.GetProperty("account").Int("userId").ShouldBe(user.ID);
        body.GetProperty("deletedReviewIds").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Changes_ContinuedPage_HasNoAccount()
    {
        var user = await Data.UserAsync();

        var body = await ExpectAsync(await As(user).GetAsync("api/Sync/changes?since=1&continued=true"), HttpStatusCode.OK);

        body.TryGetProperty("account", out _).ShouldBeFalse();
        body.Bool("fullResync").ShouldBeFalse();
    }

    [Fact]
    public async Task Visible_AnswersWithTheStillVisibleIds()
    {
        var user = await Data.UserAsync();
        var other = await Data.UserAsync();
        var shown = await Data.ReviewAsync(other);
        var hidden = await Data.ReviewAsync(other, visibility: Enums.VisibilityLevel.Private);

        var body = await ExpectAsync(await As(user).PostAsJsonAsync("api/Sync/visible",
            new { reviewIds = new[] { shown.ID, hidden.ID } }), HttpStatusCode.OK);

        body.GetProperty("reviewIds").EnumerateArray().Select(id => id.GetInt32()).ShouldBe([shown.ID]);
        body.GetProperty("collectionIds").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Visible_TooManyIds_IsABadRequest()
    {
        var user = await Data.UserAsync();

        (await As(user).PostAsJsonAsync("api/Sync/visible", new { reviewIds = Enumerable.Range(1, 1001).ToArray() }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
