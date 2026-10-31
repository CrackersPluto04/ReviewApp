using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services.Interfaces;
using ReviewApp.Api.Tests.Support;

namespace ReviewApp.Api.Tests.Integration;

public class ReviewServiceTests : IntegrationTest
{
    private IReviewService Reviews => Service<IReviewService>();

    private static ReviewMediaDto Request(Media media, decimal score = 7.5m, VisibilityLevel visibility = VisibilityLevel.Public, string? text = "Good") =>
        new()
        {
            MediaDto = new MediaDto { ID = media.ExternalApiID + media.MediaType, ExternalApiID = media.ExternalApiID, MediaType = media.MediaType },
            ReviewDto = new ReviewDto { Score = score, ReviewText = text, VisibilityLevel = visibility }
        };

    private async Task<List<JsonElement>> UserReviewIdsAsync(User target, int? viewer, ReviewFilterParams? filter = null)
    {
        var (success, page) = await Reviews.GetUserReviewsAsync(target.Username, viewer, filter ?? new ReviewFilterParams());
        success.ShouldBeTrue();
        return Json.Items(page!.Items);
    }

    /* Create / edit / delete */

    [Fact]
    public async Task Create_ForNewMedia_CreatesMediaAndReview()
    {
        var user = await Data.UserAsync();
        var externalId = TestData.Unique("7");
        var dto = new ReviewMediaDto
        {
            MediaDto = new MediaDto { ID = externalId + "Movie", ExternalApiID = externalId, MediaType = MediaType.Movie },
            ReviewDto = new ReviewDto { Score = 9, ReviewText = "Loved it", Pros = "Story", Cons = "Length", VisibilityLevel = VisibilityLevel.FollowersOnly }
        };

        var (success, _) = await Reviews.CreateReviewAsync(user.ID, dto);

        success.ShouldBeTrue();
        var review = await Query().Reviews.Include(r => r.Media).SingleAsync(r => r.UserID == user.ID);
        review.Media.ExternalApiID.ShouldBe(externalId);
        review.Score.ShouldBe(9);
        review.Pros.ShouldBe("Story");
        review.VisibilityLevel.ShouldBe(VisibilityLevel.FollowersOnly);
        // Set by the SetUpdatedAt trigger, not by the application
        review.UpdatedAt.ShouldBe(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Create_SecondReviewOfSameMedia_IsRejected()
    {
        var user = await Data.UserAsync();
        var media = await Data.MediaAsync();
        await Reviews.CreateReviewAsync(user.ID, Request(media));

        var (success, message) = await Reviews.CreateReviewAsync(user.ID, Request(media, 3));

        success.ShouldBeFalse();
        message.ShouldBe("Media already reviewed, please use edit instead.");
        (await Query().Reviews.CountAsync(r => r.UserID == user.ID)).ShouldBe(1);
    }

    [Fact]
    public async Task Create_InvalidMediaType_Fails()
    {
        var user = await Data.UserAsync();
        var dto = new ReviewMediaDto
        {
            MediaDto = new MediaDto { ID = "x", ExternalApiID = "1", MediaType = (MediaType)42 },
            ReviewDto = new ReviewDto { Score = 5 }
        };

        (await Reviews.CreateReviewAsync(user.ID, dto)).Success.ShouldBeFalse();
    }

    [Fact]
    public async Task Edit_UpdatesFieldsAndTriggerRefreshesUpdatedAt()
    {
        var user = await Data.UserAsync();
        var media = await Data.MediaAsync();
        await Reviews.CreateReviewAsync(user.ID, Request(media));
        var before = (await Query().Reviews.SingleAsync(r => r.UserID == user.ID)).UpdatedAt;
        await Task.Delay(20);

        var (success, _) = await Reviews.EditReviewAsync(user.ID, Request(media, 4, VisibilityLevel.Private, "Changed my mind"));

        success.ShouldBeTrue();
        var review = await Query().Reviews.SingleAsync(r => r.UserID == user.ID);
        review.Score.ShouldBe(4);
        review.ReviewText.ShouldBe("Changed my mind");
        review.VisibilityLevel.ShouldBe(VisibilityLevel.Private);
        review.UpdatedAt.ShouldBeGreaterThan(before);
    }

    [Fact]
    public async Task Edit_WithoutExistingReview_IsRejected()
    {
        var user = await Data.UserAsync();
        var media = await Data.MediaAsync();

        (await Reviews.EditReviewAsync(user.ID, Request(media))).Message.ShouldBe("Review not found, please use create instead.");
    }

    [Fact]
    public async Task Edit_UnknownMedia_IsRejected()
    {
        var user = await Data.UserAsync();
        var dto = new ReviewMediaDto
        {
            MediaDto = new MediaDto { ID = "x", ExternalApiID = TestData.Unique("missing"), MediaType = MediaType.Movie },
            ReviewDto = new ReviewDto { Score = 5 }
        };

        (await Reviews.EditReviewAsync(user.ID, dto)).Success.ShouldBeFalse();
    }

    [Fact]
    public async Task Delete_OnlyTheAuthorCan()
    {
        var author = await Data.UserAsync();
        var other = await Data.UserAsync();
        var review = await Data.ReviewAsync(author);

        (await Reviews.DeleteReviewAsync(other.ID, review.ID)).Success.ShouldBeFalse();
        (await Reviews.DeleteReviewAsync(author.ID, review.ID)).Success.ShouldBeTrue();
        (await Query().Reviews.AnyAsync(r => r.ID == review.ID)).ShouldBeFalse();
    }

    [Fact]
    public async Task Delete_CascadesToTheWholeReplyThread()
    {
        var author = await Data.UserAsync();
        var replier = await Data.UserAsync();
        var review = await Data.ReviewAsync(author);
        var reply = await Data.ReplyAsync(review, replier);
        await Data.ReplyAsync(review, author, parent: reply);
        await Data.ReplyAsync(review, replier, parent: reply, deleted: true);

        (await Reviews.DeleteReviewAsync(author.ID, review.ID)).Success.ShouldBeTrue();

        (await Query().ReviewReplies.AnyAsync(r => r.ReviewID == review.ID)).ShouldBeFalse();
    }

    [Fact]
    public async Task Check_ReturnsTheUsersOwnReview()
    {
        var user = await Data.UserAsync();
        var media = await Data.MediaAsync();
        await Data.ReviewAsync(user, media, VisibilityLevel.Private, 6.5m);

        var (hasReviewed, review) = await Reviews.CheckIfUserReviewedMediaAsync(user.ID, media.MediaType, media.ExternalApiID);
        var (otherHasReviewed, _) = await Reviews.CheckIfUserReviewedMediaAsync((await Data.UserAsync()).ID, media.MediaType, media.ExternalApiID);

        hasReviewed.ShouldBeTrue();
        review!.Score.ShouldBe(6.5m);
        review.VisibilityLevel.ShouldBe(VisibilityLevel.Private);
        otherHasReviewed.ShouldBeFalse();
    }

    /* Visibility */

    [Fact]
    public async Task UserReviews_VisibilityDependsOnTheViewer()
    {
        var author = await Data.UserAsync();
        var follower = await Data.UserAsync();
        var stranger = await Data.UserAsync();
        await Data.FollowAsync(follower, author);
        var @public = await Data.ReviewAsync(author, visibility: VisibilityLevel.Public);
        var followersOnly = await Data.ReviewAsync(author, visibility: VisibilityLevel.FollowersOnly);
        var @private = await Data.ReviewAsync(author, visibility: VisibilityLevel.Private);

        (await UserReviewIdsAsync(author, author.ID)).Select(r => r.Int("id")).ShouldBe([@public.ID, followersOnly.ID, @private.ID], ignoreOrder: true);
        (await UserReviewIdsAsync(author, follower.ID)).Select(r => r.Int("id")).ShouldBe([@public.ID, followersOnly.ID], ignoreOrder: true);
        (await UserReviewIdsAsync(author, stranger.ID)).Select(r => r.Int("id")).ShouldBe([@public.ID]);
        (await UserReviewIdsAsync(author, null)).Select(r => r.Int("id")).ShouldBe([@public.ID]);
    }

    [Fact]
    public async Task UserReviews_MarkTheOwnerAndUnknownUserFails()
    {
        var author = await Data.UserAsync();
        await Data.ReviewAsync(author);

        (await UserReviewIdsAsync(author, author.ID)).Single().Bool("isOwner").ShouldBeTrue();
        (await UserReviewIdsAsync(author, null)).Single().Bool("isOwner").ShouldBeFalse();
        (await Reviews.GetUserReviewsAsync(TestData.Unique("nobody"), null, new ReviewFilterParams())).Success.ShouldBeFalse();
    }

    [Fact]
    public async Task GetById_HiddenReviewIsNotFound()
    {
        var author = await Data.UserAsync();
        var follower = await Data.UserAsync();
        await Data.FollowAsync(follower, author);
        var followersOnly = await Data.ReviewAsync(author, visibility: VisibilityLevel.FollowersOnly);
        var @private = await Data.ReviewAsync(author, visibility: VisibilityLevel.Private);

        (await Reviews.GetReviewByIdAsync(followersOnly.ID, follower.ID)).ShouldNotBeNull();
        (await Reviews.GetReviewByIdAsync(followersOnly.ID, null)).ShouldBeNull();
        (await Reviews.GetReviewByIdAsync(@private.ID, follower.ID)).ShouldBeNull();
        (await Reviews.GetReviewByIdAsync(@private.ID, author.ID)).ShouldNotBeNull();
        (await Reviews.GetReviewByIdAsync(-1, author.ID)).ShouldBeNull();
    }

    [Fact]
    public async Task GetById_IncludesAuthorMediaAndNonDeletedReplyCount()
    {
        var author = await Data.UserAsync();
        var media = await Data.MediaAsync(title: "Dune");
        var review = await Data.ReviewAsync(author, media);
        await Data.ReplyAsync(review, author);
        await Data.ReplyAsync(review, author, deleted: true);

        var json = Json.Of(await Reviews.GetReviewByIdAsync(review.ID, null));

        json.Str("username").ShouldBe(author.Username);
        json.Str("title").ShouldBe("Dune");
        json.Int("replyCount").ShouldBe(1);
        json.Bool("isOwner").ShouldBeFalse();
    }

    [Fact]
    public async Task MediaReviews_OnlyPublicOnesEvenForFollowers()
    {
        var author = await Data.UserAsync();
        var media = await Data.MediaAsync();
        var @public = await Data.ReviewAsync(author, media, VisibilityLevel.Public);
        await Data.ReviewAsync(await Data.UserAsync(), media, VisibilityLevel.FollowersOnly);
        await Data.ReviewAsync(await Data.UserAsync(), media, VisibilityLevel.Private);

        var page = await Reviews.GetMediaReviewsAsync(media.MediaType, media.ExternalApiID, new ReviewFilterParams());

        page.TotalCount.ShouldBe(1);
        Json.Items(page.Items).Single().Int("id").ShouldBe(@public.ID);
    }

    [Fact]
    public async Task MediaReviews_UnreviewedMedia_IsAnEmptyPage()
    {
        var page = await Reviews.GetMediaReviewsAsync(MediaType.Movie, TestData.Unique("none"), new ReviewFilterParams());

        page.TotalCount.ShouldBe(0);
        page.Items.ShouldBeEmpty();
        page.TotalPages.ShouldBe(0);
    }

    /* Filters, sorting, paging */

    private async Task<(Media Media, Review Low, Review Mid, Review High)> ThreeReviewsAsync()
    {
        var media = await Data.MediaAsync();
        var now = DateTime.UtcNow;
        var low = await Data.ReviewAsync(await Data.UserAsync(), media, score: 2, text: null, createdAt: now.AddDays(-3));
        var mid = await Data.ReviewAsync(await Data.UserAsync(), media, score: 5.5m, text: "Okay", createdAt: now.AddDays(-2));
        var high = await Data.ReviewAsync(await Data.UserAsync(), media, score: 9, text: "Great", createdAt: now.AddDays(-1));
        return (media, low, mid, high);
    }

    private async Task<List<int>> MediaReviewIdsAsync(Media media, ReviewFilterParams filter) =>
        Json.Items((await Reviews.GetMediaReviewsAsync(media.MediaType, media.ExternalApiID, filter)).Items).Select(r => r.Int("id")).ToList();

    [Theory]
    [InlineData("created_desc", new[] { 2, 1, 0 })]
    [InlineData("created_asc", new[] { 0, 1, 2 })]
    [InlineData("score_desc", new[] { 2, 1, 0 })]
    [InlineData("score_asc", new[] { 0, 1, 2 })]
    [InlineData("nonsense", new[] { 2, 1, 0 })]
    public async Task MediaReviews_Sort(string sortBy, int[] expectedOrder)
    {
        var (media, low, mid, high) = await ThreeReviewsAsync();
        var byIndex = new[] { low.ID, mid.ID, high.ID };

        (await MediaReviewIdsAsync(media, new ReviewFilterParams { SortBy = sortBy })).ShouldBe(expectedOrder.Select(i => byIndex[i]));
    }

    [Fact]
    public async Task MediaReviews_ScoreRangeAndWrittenTextFilters()
    {
        var (media, low, mid, high) = await ThreeReviewsAsync();

        (await MediaReviewIdsAsync(media, new ReviewFilterParams { MinScore = 5, MaxScore = 9 })).ShouldBe([high.ID, mid.ID]);
        (await MediaReviewIdsAsync(media, new ReviewFilterParams { HasWrittenText = true })).ShouldNotContain(low.ID);
    }

    [Fact]
    public async Task MediaReviews_PagesWithTotals()
    {
        var (media, _, mid, _) = await ThreeReviewsAsync();

        var page = await Reviews.GetMediaReviewsAsync(media.MediaType, media.ExternalApiID, new ReviewFilterParams { Page = 2, PageSize = 1 });

        page.TotalCount.ShouldBe(3);
        page.TotalPages.ShouldBe(3);
        page.CurrentPage.ShouldBe(2);
        Json.Items(page.Items).Single().Int("id").ShouldBe(mid.ID);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-3, 10)]
    [InlineData(1, 0)]
    [InlineData(1, -5)]
    public async Task Paging_InvalidPageOrPageSize_IsClampedInsteadOfFailing(int page, int pageSize)
    {
        var (media, _, _, _) = await ThreeReviewsAsync();
        var author = await Data.UserAsync();
        await Data.ReviewAsync(author);
        var filter = new ReviewFilterParams { Page = page, PageSize = pageSize };

        var mediaPage = await Reviews.GetMediaReviewsAsync(media.MediaType, media.ExternalApiID, filter);
        var (success, userPage) = await Reviews.GetUserReviewsAsync(author.Username, null, filter);

        mediaPage.Items.ShouldNotBeEmpty();
        mediaPage.CurrentPage.ShouldBe(1);
        mediaPage.TotalPages.ShouldBeGreaterThan(0);
        success.ShouldBeTrue();
        userPage!.Items.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Paging_PageSizeIsCappedAtFifty()
    {
        var media = await Data.MediaAsync();
        var author = await Data.UserAsync();
        for (var i = 0; i < 51; i++)
            await Data.ReviewAsync(author, media);

        var page = await Reviews.GetMediaReviewsAsync(media.MediaType, media.ExternalApiID, new ReviewFilterParams { PageSize = 100_000 });

        page.Items.Count().ShouldBe(50);
        page.TotalPages.ShouldBe(2);
    }

    /* Stats and recent */

    [Fact]
    public async Task AverageScore_CountsPublicReviewsOnlyAndRoundsToOneDecimal()
    {
        var media = await Data.MediaAsync();
        await Data.ReviewAsync(await Data.UserAsync(), media, score: 7);
        await Data.ReviewAsync(await Data.UserAsync(), media, score: 8);
        await Data.ReviewAsync(await Data.UserAsync(), media, score: 8);
        await Data.ReviewAsync(await Data.UserAsync(), media, VisibilityLevel.Private, score: 1);

        var (average, count) = await Reviews.GetAverageScoreAsync(media.MediaType, media.ExternalApiID);

        count.ShouldBe(3);
        average.ShouldBe(7.7m);
    }

    [Fact]
    public async Task AverageScore_UnknownMedia_IsZero()
    {
        (await Reviews.GetAverageScoreAsync(MediaType.Music, TestData.Unique("none"))).ShouldBe((0m, 0));
    }

    [Fact]
    public async Task Recent_NewestPublicReviewsWithText()
    {
        var author = await Data.UserAsync();
        var future = DateTime.UtcNow.AddYears(5); // newer than anything other tests create
        var withText = await Data.ReviewAsync(author, text: "Recent", createdAt: future);
        var older = await Data.ReviewAsync(author, text: "Older", createdAt: future.AddMinutes(-1));
        await Data.ReviewAsync(author, text: null, createdAt: future.AddMinutes(1));
        await Data.ReviewAsync(author, visibility: VisibilityLevel.FollowersOnly, text: "Hidden", createdAt: future.AddMinutes(2));

        var recent = Json.Items(await Reviews.GetRecentPublicReviewsAsync(2));

        recent.Select(r => r.Int("id")).ShouldBe([withText.ID, older.ID]);
        recent[0].Str("username").ShouldBe(author.Username);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(500, 20)]
    public async Task Recent_CountIsClamped(int requested, int max)
    {
        var author = await Data.UserAsync();
        for (var i = 0; i < 2; i++)
            await Data.ReviewAsync(author);

        (await Reviews.GetRecentPublicReviewsAsync(requested)).Count.ShouldBeLessThanOrEqualTo(max);
    }
}
