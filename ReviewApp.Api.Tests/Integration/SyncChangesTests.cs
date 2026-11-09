using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services.Interfaces;
using ReviewApp.Api.Services.Sync;
using ReviewApp.Api.Tests.Support;

namespace ReviewApp.Api.Tests.Integration;

// SyncChangesService: the changes feed of the user's own content (rowversion cursor, tombstones, paging, full resync,
// account data) and the visibility check of other users' cached content
public class SyncChangesTests : IntegrationTest
{
    private ISyncChangesService Changes => Service<ISyncChangesService>();

    // After every write is committed (see AllWritesCommittedAsync)
    private async Task<SyncChangesDto> FeedAsync(User user, long since = 0, DateTime? syncedAt = null, bool continued = false,
        int limit = SyncChangesService.DefaultLimit)
    {
        await AllWritesCommittedAsync();

        return await Changes.GetChangesAsync(user.ID, since, syncedAt, continued, limit);
    }

    // Everything of one run: pages until nothing is left
    private async Task<List<SyncChangesDto>> AllPagesAsync(User user, long since, int limit)
    {
        var pages = new List<SyncChangesDto> { await FeedAsync(user, since, limit: limit) };
        while (pages[^1].HasMore)
            pages.Add(await FeedAsync(user, pages[^1].Cursor, continued: true, limit: limit));
        return pages;
    }

    [Fact]
    public async Task FirstSync_SendsAllOwnContentOfEveryVisibility_AndNothingOfOthers()
    {
        var user = await Data.UserAsync();
        var other = await Data.UserAsync();
        var publicReview = await Data.ReviewAsync(user);
        var privateReview = await Data.ReviewAsync(user, visibility: VisibilityLevel.Private);
        var media = await Data.MediaAsync();
        var collection = await Data.CollectionAsync(user, VisibilityLevel.Private, media: media);
        await Data.ReviewAsync(other);
        await Data.CollectionAsync(other);

        var feed = await FeedAsync(user);

        feed.FullResync.ShouldBeTrue();
        feed.HasMore.ShouldBeFalse();
        feed.Reviews.Select(r => r.Id).Order().ShouldBe(new[] { publicReview.ID, privateReview.ID }.Order());
        var favourites = await Data.FavouritesAsync(user);
        feed.Collections.Select(c => c.Id).Order().ShouldBe(new[] { favourites.ID, collection.ID }.Order());
        feed.Collections.Single(c => c.Id == favourites.ID).IsDefault.ShouldBeTrue();
        feed.Collections.Single(c => c.Id == collection.ID).Items.Single().Media.DbMediaId.ShouldBe(media.ID);
        var review = feed.Reviews.Single(r => r.Id == privateReview.ID);
        review.Visibility.ShouldBe(VisibilityLevel.Private);
        review.CreatedAt.Kind.ShouldBe(DateTimeKind.Utc);
        review.Version.ShouldBe((await Query().Reviews.SingleAsync(r => r.ID == privateReview.ID)).RowVersion);
        feed.Cursor.ShouldBeGreaterThan(feed.Reviews.Max(r => r.Version));
    }

    [Fact]
    public async Task NextSync_SendsOnlyWhatChanged_IncludingDeletionsAndItemChanges()
    {
        var user = await Data.UserAsync();
        var edited = await Data.ReviewAsync(user);
        var deleted = await Data.ReviewAsync(user);
        var untouched = await Data.ReviewAsync(user);
        var gainsAnItem = await Data.CollectionAsync(user);
        var goingAway = await Data.CollectionAsync(user);
        var first = await FeedAsync(user);

        await Query().Reviews.Where(r => r.ID == edited.ID).ExecuteUpdateAsync(s => s.SetProperty(r => r.ReviewText, "Edited on the web"));
        await Service<IReviewService>().DeleteReviewAsync(user.ID, deleted.ID);
        var created = await Data.ReviewAsync(user);
        var media = await Data.MediaAsync();
        await Service<ICollectionService>().AddMediaToCollectionAsync(user.ID, gainsAnItem.ID, media.MediaType, media.ExternalApiID);
        await Service<ICollectionService>().DeleteCollectionAsync(user.ID, goingAway.ID);

        var next = await FeedAsync(user, first.Cursor, syncedAt: first.ServerTime);

        next.FullResync.ShouldBeFalse();
        next.Reviews.Select(r => r.Id).Order().ShouldBe(new[] { edited.ID, created.ID }.Order());
        next.Reviews.Single(r => r.Id == edited.ID).ReviewText.ShouldBe("Edited on the web");
        next.DeletedReviewIds.ShouldBe([deleted.ID]);
        next.Collections.Single().ShouldSatisfyAllConditions(
            c => c.Id.ShouldBe(gainsAnItem.ID),
            c => c.Items.Single().Media.ExternalApiId.ShouldBe(media.ExternalApiID));
        next.DeletedCollectionIds.ShouldBe([goingAway.ID]);
        next.Reviews.ShouldNotContain(r => r.Id == untouched.ID);
    }

    [Fact]
    public async Task NothingChanged_IsAnEmptyPage_WithACursorThatNeverGoesBack()
    {
        var user = await Data.UserAsync();
        await Data.ReviewAsync(user);
        var first = await FeedAsync(user);

        var next = await FeedAsync(user, first.Cursor, syncedAt: first.ServerTime);

        next.Reviews.ShouldBeEmpty();
        next.Collections.ShouldBeEmpty();
        next.DeletedReviewIds.ShouldBeEmpty();
        next.Cursor.ShouldBeGreaterThanOrEqualTo(first.Cursor);
    }

    [Fact]
    public async Task Paging_SendsEveryChangeOnce_AndAccountDataOnTheFirstPageOnly()
    {
        var user = await Data.UserAsync();
        var reviews = new List<Review>();
        for (var i = 0; i < 5; i++)
            reviews.Add(await Data.ReviewAsync(user));
        var collection = await Data.CollectionAsync(user);
        await Service<IReviewService>().DeleteReviewAsync(user.ID, reviews[0].ID);

        var pages = await AllPagesAsync(user, since: 0, limit: 2);

        // 4 reviews + Favourites + the collection + 1 deletion = 7 changes, 2 per page
        pages.Count.ShouldBe(4);
        pages.SelectMany(p => p.Reviews).Select(r => r.Id).ShouldBe(reviews.Skip(1).Select(r => r.ID), ignoreOrder: true);
        pages.SelectMany(p => p.Collections).Select(c => c.Id).ShouldContain(collection.ID);
        pages.SelectMany(p => p.DeletedReviewIds).ShouldBe([reviews[0].ID]);
        pages[0].Account.ShouldNotBeNull();
        pages.Skip(1).ShouldAllBe(p => p.Account == null && !p.FullResync);
        pages.Select(p => p.Cursor).ShouldBeInOrder();
    }

    [Fact]
    public async Task LastSyncTooLongAgo_IsAFullResync()
    {
        // Tombstones are cleaned up after the retention, so a device away longer could have missed a deletion
        var user = await Data.UserAsync();
        var review = await Data.ReviewAsync(user);
        var first = await FeedAsync(user);

        var stale = await FeedAsync(user, first.Cursor, syncedAt: DateTime.UtcNow - SyncRetention.Retention);

        stale.FullResync.ShouldBeTrue();
        stale.Reviews.Select(r => r.Id).ShouldBe([review.ID]);
    }

    [Fact]
    public async Task Account_HasTheProfileFollowsAndNotifications()
    {
        var user = await Data.UserAsync(u => u.Bio = "My bio");
        var followed = await Data.UserAsync();
        var follower = await Data.UserAsync();
        await Data.FollowAsync(user, followed);
        await Data.FollowAsync(follower, user);
        await Data.NotificationsAsync(user, 3);

        var account = (await FeedAsync(user)).Account!;

        (account.UserId, account.Username, account.Bio).ShouldBe((user.ID, user.Username, "My bio"));
        account.Following.Select(u => u.ID).ShouldBe([followed.ID]);
        account.Followers.Single().ShouldSatisfyAllConditions(
            u => u.ID.ShouldBe(follower.ID),
            u => u.IsFollowedByCurrentUser.ShouldBe(false));
        account.Notifications.Count.ShouldBe(3);
        account.Notifications.Select(n => n.ID).ShouldBeInOrder(SortDirection.Descending);
        account.UnseenNotificationCount.ShouldBe(3);
    }

    /* Visibility of other users' cached content */

    [Fact]
    public async Task Visible_FollowsTheSameRulesAsTheSinglePages()
    {
        var user = await Data.UserAsync();
        var followed = await Data.UserAsync();
        var stranger = await Data.UserAsync();
        await Data.FollowAsync(user, followed);
        var own = await Data.ReviewAsync(user, visibility: VisibilityLevel.Private);
        var publicReview = await Data.ReviewAsync(stranger);
        var privateReview = await Data.ReviewAsync(stranger, visibility: VisibilityLevel.Private);
        var forFollowers = await Data.ReviewAsync(followed, visibility: VisibilityLevel.FollowersOnly);
        var strangersForFollowers = await Data.ReviewAsync(stranger, visibility: VisibilityLevel.FollowersOnly);
        var publicCollection = await Data.CollectionAsync(stranger);
        var hiddenCollection = await Data.CollectionAsync(stranger, VisibilityLevel.Private);
        var followersCollection = await Data.CollectionAsync(followed, VisibilityLevel.FollowersOnly);

        var visible = await Changes.GetVisibleAsync(user.ID,
            [own.ID, publicReview.ID, privateReview.ID, forFollowers.ID, strangersForFollowers.ID, int.MaxValue],
            [publicCollection.ID, hiddenCollection.ID, followersCollection.ID]);

        visible.ReviewIds.ShouldBe(new[] { own.ID, publicReview.ID, forFollowers.ID }.Order());
        visible.CollectionIds.ShouldBe(new[] { publicCollection.ID, followersCollection.ID }.Order());
    }

    [Fact]
    public async Task Visible_AfterUnfollowing_FollowersOnlyContentIsGone()
    {
        var user = await Data.UserAsync();
        var followed = await Data.UserAsync();
        await Data.FollowAsync(user, followed);
        var review = await Data.ReviewAsync(followed, visibility: VisibilityLevel.FollowersOnly);
        await Service<IFollowerService>().UnfollowUserAsync(user.ID, followed.ID);

        (await Changes.GetVisibleAsync(user.ID, [review.ID], [])).ReviewIds.ShouldBeEmpty();
    }
}
