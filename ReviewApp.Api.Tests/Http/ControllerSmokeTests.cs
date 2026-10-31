using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Tests.Support;

namespace ReviewApp.Api.Tests.Http;

// One success and one failure per controller: routing, model binding, status codes and response shapes are wired up.
// The behaviour behind them is covered by the integration tests.
public class ControllerSmokeTests : HttpTest
{
    /* User */

    [Fact]
    public async Task User_ProfileIsPublicAndHidesPrivateFields()
    {
        var user = await Data.UserAsync(u => u.Bio = "Hi");

        var response = await Anonymous().GetAsync($"api/User/{user.Username}");
        var json = await response.Content.ReadAsStringAsync();

        (await ExpectAsync(response, HttpStatusCode.OK)).Str("bio").ShouldBe("Hi");
        json.ShouldNotContain(user.Email);
        json.ShouldNotContain("passwordHash", Case.Insensitive);
        json.ShouldNotContain("tokenVersion", Case.Insensitive);
        (await ErrorAsync(await Anonymous().GetAsync($"api/User/{TestData.Unique("nobody")}"), HttpStatusCode.NotFound)).ShouldBe("User profile not found.");
    }

    [Fact]
    public async Task User_UpdateOwnProfile()
    {
        var user = await Data.UserAsync();
        var client = As(user);

        await ExpectAsync(await client.PatchAsJsonAsync("api/User/me", new { bio = "New bio" }), HttpStatusCode.OK);
        (await ExpectAsync(await client.PatchAsJsonAsync("api/User/me", new { profilePictureUrl = "nope" }), HttpStatusCode.BadRequest))
            .GetProperty("errors").TryGetProperty("ProfilePictureUrl", out _).ShouldBeTrue();
        (await Anonymous().PatchAsJsonAsync("api/User/me", new { bio = "x" })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task User_SearchListsAndFollowerListsWork()
    {
        var token = TestData.Unique("smoke");
        var target = await Data.UserAsync(u => u.Username = token);
        var fan = await Data.UserAsync();
        await Data.FollowAsync(fan, target);

        (await ExpectAsync(await Anonymous().GetAsync($"api/User/search?q={token}"), HttpStatusCode.OK)).GetArrayLength().ShouldBe(1);
        (await ExpectAsync(await Anonymous().GetAsync($"api/User/{token}/followers"), HttpStatusCode.OK))[0].Str("username").ShouldBe(fan.Username);
        (await ExpectAsync(await Anonymous().GetAsync($"api/User/{fan.Username}/following"), HttpStatusCode.OK))[0].Str("username").ShouldBe(token);
        (await ExpectAsync(await Anonymous().GetAsync($"api/User/{token}/collections"), HttpStatusCode.OK)).GetArrayLength().ShouldBe(0);
        (await ExpectAsync(await Anonymous().GetAsync($"api/User/{token}/reviews"), HttpStatusCode.OK)).Int("totalCount").ShouldBe(0);
        (await Anonymous().GetAsync($"api/User/{TestData.Unique("nobody")}/reviews")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact(Skip = "Known issue F2 (FINDINGS.md): the followers / following lists answer 200 [] for an unknown user, the other profile lists 404")]
    public async Task User_FollowerListsOfAnUnknownUser_Are404()
    {
        var name = TestData.Unique("nobody");

        (await Anonymous().GetAsync($"api/User/{name}/followers")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Anonymous().GetAsync($"api/User/{name}/following")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact(Skip = "Known issue F3 (FINDINGS.md): 'search' is a valid username, but GET api/User/search is the user search route")]
    public async Task User_NamedSearch_HasAReachableProfile()
    {
        var user = await Data.UserAsync(u => u.Username = "search");

        (await ExpectAsync(await Anonymous().GetAsync("api/User/search"), HttpStatusCode.OK)).Int("id").ShouldBe(user.ID);
    }

    /* Reviews */

    [Fact]
    public async Task Review_CreateReadAndDelete()
    {
        var user = await Data.UserAsync();
        var client = As(user);
        var externalId = TestData.Unique("8");
        var request = new
        {
            mediaDto = new { id = externalId + "Movie", externalApiID = externalId, mediaType = MediaType.Movie },
            reviewDto = new { score = 7.5, reviewText = "Fine", visibilityLevel = VisibilityLevel.Public }
        };

        await ExpectAsync(await client.PostAsJsonAsync("api/Review", request), HttpStatusCode.OK);
        (await ErrorAsync(await client.PostAsJsonAsync("api/Review", request), HttpStatusCode.BadRequest)).ShouldBe("Media already reviewed, please use edit instead.");

        var check = await ExpectAsync(await client.GetAsync($"api/Review/check?externalApiId={externalId}&mediaType=Movie"), HttpStatusCode.OK);
        check.Bool("hasReviewed").ShouldBeTrue();
        var page = await ExpectAsync(await Anonymous().GetAsync($"api/Review/media/Movie/{externalId}"), HttpStatusCode.OK);
        var id = page.GetProperty("items")[0].Int("id");
        (await ExpectAsync(await Anonymous().GetAsync($"api/Review/stats/average-score?externalApiId={externalId}&mediaType=Movie"), HttpStatusCode.OK))
            .Dec("averageScore").ShouldBe(7.5m);

        (await As(await Data.UserAsync()).DeleteAsync($"api/Review/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.DeleteAsync($"api/Review/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Review_ScoreOutOfRange_IsAValidationProblem()
    {
        var user = await Data.UserAsync();
        var externalId = TestData.Unique("8");

        var body = await ExpectAsync(await As(user).PostAsJsonAsync("api/Review", new
        {
            mediaDto = new { id = externalId, externalApiID = externalId, mediaType = MediaType.Movie },
            reviewDto = new { score = 11 }
        }), HttpStatusCode.BadRequest);

        body.GetProperty("errors").TryGetProperty("ReviewDto.Score", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Review_SingleAndRecentAreAnonymous()
    {
        var review = await Data.ReviewAsync(await Data.UserAsync());

        (await ExpectAsync(await Anonymous().GetAsync($"api/Review/{review.ID}"), HttpStatusCode.OK)).Int("id").ShouldBe(review.ID);
        (await ExpectAsync(await Anonymous().GetAsync("api/Review/recent?count=3"), HttpStatusCode.OK)).GetArrayLength().ShouldBeLessThanOrEqualTo(3);
    }

    /* Replies */

    [Fact]
    public async Task Reply_CreateListGetAndDelete()
    {
        var author = await Data.UserAsync();
        var review = await Data.ReviewAsync(author);
        var client = As(await Data.UserAsync());

        var reply = await ExpectAsync(await client.PostAsJsonAsync("api/ReviewReply", new { reviewID = review.ID, content = "Agreed" }), HttpStatusCode.OK);
        var id = reply.Int("id");

        (await ExpectAsync(await Anonymous().GetAsync($"api/ReviewReply?reviewId={review.ID}"), HttpStatusCode.OK)).GetProperty("items").GetArrayLength().ShouldBe(1);
        (await ExpectAsync(await Anonymous().GetAsync($"api/ReviewReply/{id}"), HttpStatusCode.OK)).Int("reviewID").ShouldBe(review.ID);
        (await client.DeleteAsync($"api/ReviewReply/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Anonymous().GetAsync($"api/ReviewReply/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reply_ToAHiddenReview_Is404AndToAPrivateOwnReview400()
    {
        var author = await Data.UserAsync();
        var hidden = await Data.ReviewAsync(author, visibility: VisibilityLevel.Private);

        (await As(await Data.UserAsync()).PostAsJsonAsync("api/ReviewReply", new { reviewID = hidden.ID, content = "Hi" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ErrorAsync(await As(author).PostAsJsonAsync("api/ReviewReply", new { reviewID = hidden.ID, content = "Hi" }), HttpStatusCode.BadRequest))
            .ShouldBe("Private reviews can't be replied to.");
    }

    /* Collections */

    [Fact]
    public async Task Collection_CreateAddReorderRemoveDelete()
    {
        var user = await Data.UserAsync();
        var client = As(user);

        var created = await client.PostAsJsonAsync("api/Collection", new { name = "Weekend", visibilityLevel = VisibilityLevel.Public });
        var collection = await ExpectAsync(created, HttpStatusCode.Created);
        var id = collection.Int("id");
        created.Headers.Location!.ToString().ShouldEndWith($"/api/Collection/{id}");

        var externalId = TestData.Unique("8");
        await ExpectAsync(await client.PostAsJsonAsync($"api/Collection/{id}/media", new { type = MediaType.Movie, externalApiID = externalId }), HttpStatusCode.OK);
        (await ErrorAsync(await client.PostAsJsonAsync($"api/Collection/{id}/media", new { type = MediaType.Movie, externalApiID = externalId }), HttpStatusCode.BadRequest))
            .ShouldBe("Failed to add media. It may already exist in this collection.");

        var opened = await ExpectAsync(await Anonymous().GetAsync($"api/Collection/{id}"), HttpStatusCode.OK);
        var dbMediaId = opened.GetProperty("mediaItems")[0].Int("dbMediaID");
        await ExpectAsync(await client.PutAsJsonAsync($"api/Collection/{id}/reorder", new { dbMediaID = dbMediaId, newOrderIndex = 0 }), HttpStatusCode.OK);
        (await ExpectAsync(await client.GetAsync("api/Collection/me/memberships"), HttpStatusCode.OK)).GetArrayLength().ShouldBe(1);

        (await client.DeleteAsync($"api/Collection/{id}/media/Movie-{externalId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.DeleteAsync($"api/Collection/{id}/media/Movie-{externalId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.DeleteAsync($"api/Collection/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Collection_FavouritesIsLockedAndPrivateOnesAreHidden()
    {
        var user = await Data.UserAsync();
        var favourites = await Data.FavouritesAsync(user);
        var client = As(user);

        (await ErrorAsync(await client.DeleteAsync($"api/Collection/{favourites.ID}"), HttpStatusCode.BadRequest))
            .ShouldBe("Your Favourites collection can't be renamed, edited or deleted.");
        (await ErrorAsync(await Anonymous().GetAsync($"api/Collection/{favourites.ID}"), HttpStatusCode.NotFound)).ShouldBe("Collection not found or is private.");
        (await ExpectAsync(await client.PutAsJsonAsync("api/Collection", new { collectionID = -1, name = "x" }), HttpStatusCode.NotFound)).Str("error").ShouldNotBeNull();
    }

    /* Followers */

    [Fact]
    public async Task Follower_FollowUnfollowRemove()
    {
        var me = await Data.UserAsync();
        var other = await Data.UserAsync();
        var client = As(me);

        await ExpectAsync(await client.PostAsync($"api/Follower/{other.ID}/follow", null), HttpStatusCode.OK);
        (await ErrorAsync(await client.PostAsync($"api/Follower/{other.ID}/follow", null), HttpStatusCode.BadRequest)).ShouldBe("You already follow this user.");
        await ExpectAsync(await client.DeleteAsync($"api/Follower/{other.ID}/unfollow"), HttpStatusCode.OK);
        (await ErrorAsync(await client.DeleteAsync($"api/Follower/{other.ID}/remove"), HttpStatusCode.BadRequest)).ShouldBe("This user is not following you.");
        (await Anonymous().PostAsync($"api/Follower/{other.ID}/follow", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /* Media */

    [Fact]
    public async Task Media_DetailsSearchAndDiscover()
    {
        var externalId = TestData.Unique("8");

        (await ExpectAsync(await Anonymous().GetAsync($"api/Media/media/Movie/{externalId}"), HttpStatusCode.OK)).Str("externalApiID").ShouldBe(externalId);
        (await ExpectAsync(await Anonymous().GetAsync("api/Media/search/movie?query=alien"), HttpStatusCode.OK)).Int("totalCount").ShouldBe(1);
        (await ExpectAsync(await Anonymous().GetAsync("api/Media/search/all?query=alien"), HttpStatusCode.OK)).GetProperty("items").GetArrayLength().ShouldBeGreaterThan(0);
        (await ExpectAsync(await Anonymous().GetAsync("api/Media/discover/music"), HttpStatusCode.OK)).GetProperty("items").GetArrayLength().ShouldBe(1);
        // A blank query never reaches the action: model binding turns it into a missing required value
        (await Anonymous().GetAsync("api/Media/search/series?query=%20")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Anonymous().GetAsync("api/Media/search/series")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Anonymous().GetAsync("api/Media/media/Book/1")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /* Notifications */

    [Fact]
    public async Task Notification_ListCountMarkAndDelete()
    {
        var user = await Data.UserAsync();
        var notification = await Data.NotificationAsync(user);
        var foreign = await Data.NotificationAsync(await Data.UserAsync());
        var client = As(user);

        (await ExpectAsync(await client.GetAsync("api/Notification"), HttpStatusCode.OK)).GetProperty("items").GetArrayLength().ShouldBe(1);
        (await ExpectAsync(await client.GetAsync("api/Notification/unseen-count"), HttpStatusCode.OK)).Int("count").ShouldBe(1);
        (await client.PatchAsJsonAsync($"api/Notification/{notification.ID}/seen", new { isSeen = true })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.PatchAsJsonAsync($"api/Notification/{foreign.ID}/seen", new { isSeen = true })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ExpectAsync(await client.PostAsync("api/Notification/seen-all", null), HttpStatusCode.OK)).Int("updated").ShouldBe(0);
        (await client.DeleteAsync($"api/Notification/{foreign.ID}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ExpectAsync(await client.DeleteAsync("api/Notification/seen"), HttpStatusCode.OK)).Int("deleted").ShouldBe(1);
        (await client.GetAsync("api/Notification?pageSize=500")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Notification_SetSeenWithoutAValue_IsRejected()
    {
        var user = await Data.UserAsync();
        var notification = await Data.NotificationAsync(user, seen: true);

        (await As(user).PatchAsJsonAsync($"api/Notification/{notification.ID}/seen", new { })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await Query().Notifications.SingleAsync(n => n.ID == notification.ID)).IsSeen.ShouldBeTrue();
    }

    /* Security */

    [Fact]
    public async Task Security_StatusVerifyAndPendingEmail()
    {
        var user = await Data.UserAsync(u => u.IsEmailConfirmed = false);
        var (_, token) = await Data.SecurityTokenAsync(user, SecurityTokenType.EmailConfirmation);
        var client = As(user);

        (await ExpectAsync(await client.GetAsync("api/Security/status"), HttpStatusCode.OK)).Bool("isEmailConfirmed").ShouldBeFalse();
        (await ErrorAsync(await Anonymous().PostAsJsonAsync("api/Security/verify-email", new { token = "wrong" }), HttpStatusCode.BadRequest))
            .ShouldBe("This verification link is invalid or has expired.");
        await ExpectAsync(await Anonymous().PostAsJsonAsync("api/Security/verify-email", new { token }), HttpStatusCode.OK);
        (await ErrorAsync(await client.DeleteAsync("api/Security/pending-email"), HttpStatusCode.NotFound)).ShouldBe("There is no pending email change.");
    }

    [Fact]
    public async Task Security_TwoFactorEnableOverHttp()
    {
        var user = await Data.UserAsync();
        var client = As(user);

        (await ErrorAsync(await client.PostAsJsonAsync("api/Security/2fa/enable/request", new { currentPassword = "wrong" }), HttpStatusCode.BadRequest))
            .ShouldBe("Incorrect password.");
        await ExpectAsync(await client.PostAsJsonAsync("api/Security/2fa/enable/request", new { currentPassword = TestData.Password }), HttpStatusCode.OK);
        var code = CapturingEmailSender.Code(await App.Mail.WaitForAsync(user.Email, "enable two-factor"));
        await ExpectAsync(await client.PostAsJsonAsync("api/Security/2fa/enable/confirm", new { code }), HttpStatusCode.OK);

        (await ExpectAsync(await client.GetAsync("api/Security/status"), HttpStatusCode.OK)).Bool("isTwoFactorEnabled").ShouldBeTrue();
    }

    /* Achievements */

    [Fact]
    public async Task Achievement_ListIsPublic()
    {
        var user = await Data.UserAsync();

        (await ExpectAsync(await Anonymous().GetAsync($"api/Achievement/{user.Username}"), HttpStatusCode.OK)).GetArrayLength().ShouldBe(7);
        (await Anonymous().GetAsync($"api/Achievement/{TestData.Unique("nobody")}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
