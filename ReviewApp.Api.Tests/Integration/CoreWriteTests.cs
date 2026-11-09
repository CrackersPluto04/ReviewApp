using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services;
using ReviewApp.Api.Services.Interfaces;
using ReviewApp.Api.Tests.Support;

namespace ReviewApp.Api.Tests.Integration;

// The core writes behind the public service methods (also used by the offline sync): every status they can report,
// and that they leave the side effects (achievements, notifications) to their caller
public class CoreWriteTests : IntegrationTest
{
    private IReviewService Reviews => Service<IReviewService>();
    private ICollectionService Collections => Service<ICollectionService>();
    private IFollowerService Followers => Service<IFollowerService>();
    private INotificationService Notifications => Service<INotificationService>();

    private static readonly ReviewFields Fields = new(6.5m, "Text", "Pro", "Con", VisibilityLevel.FollowersOnly);

    /* Reviews */

    [Fact]
    public async Task ReviewCreate_StoresTheFieldsAndTheGivenTime_WithoutEvaluatingAchievements()
    {
        var user = await Data.UserAsync();
        var media = await Data.MediaAsync();
        var writtenAt = DateTime.UtcNow.AddDays(-3);

        var result = await Reviews.CreateCoreAsync(user.ID, media.ID, Fields, writtenAt);

        result.Status.ShouldBe(ReviewWriteStatus.Created);
        var stored = await Query().Reviews.SingleAsync(r => r.ID == result.Review!.ID);
        stored.Score.ShouldBe(6.5m);
        stored.Pros.ShouldBe("Pro");
        stored.VisibilityLevel.ShouldBe(VisibilityLevel.FollowersOnly);
        stored.CreatedAt.ShouldBe(writtenAt, TimeSpan.FromSeconds(1));
        (await Query().UserAchievements.AnyAsync(a => a.UserID == user.ID)).ShouldBeFalse();
    }

    [Fact]
    public async Task ReviewCreate_SecondOne_IsAlreadyReviewedWithTheExistingReview()
    {
        var user = await Data.UserAsync();
        var existing = await Data.ReviewAsync(user);

        var result = await Reviews.CreateCoreAsync(user.ID, existing.MediaID, Fields);

        result.Status.ShouldBe(ReviewWriteStatus.AlreadyReviewed);
        result.Review!.ID.ShouldBe(existing.ID);
    }

    [Fact]
    public async Task ReviewUpdateAndDelete_OnlyTouchTheOwnersReview()
    {
        var owner = await Data.UserAsync();
        var stranger = await Data.UserAsync();
        var review = await Data.ReviewAsync(owner);

        (await Reviews.UpdateCoreAsync(stranger.ID, review.ID, Fields)).Status.ShouldBe(ReviewWriteStatus.NotFound);
        (await Reviews.DeleteCoreAsync(stranger.ID, review.ID)).Status.ShouldBe(ReviewWriteStatus.NotFound);
        (await Reviews.UpdateCoreAsync(owner.ID, review.ID, Fields)).Status.ShouldBe(ReviewWriteStatus.Updated);
        (await Query().Reviews.SingleAsync(r => r.ID == review.ID)).ReviewText.ShouldBe("Text");
        (await Reviews.DeleteCoreAsync(owner.ID, review.ID)).Status.ShouldBe(ReviewWriteStatus.Deleted);
        (await Reviews.DeleteCoreAsync(owner.ID, review.ID)).Status.ShouldBe(ReviewWriteStatus.NotFound);
    }

    /* Collections */

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123456789012345678901234567890123456789012345678901")]
    public async Task CollectionCreate_InvalidName(string name)
    {
        var user = await Data.UserAsync();

        (await Collections.CreateCoreAsync(user.ID, name, VisibilityLevel.Public)).Status.ShouldBe(CollectionWriteStatus.InvalidName);
    }

    [Fact]
    public async Task CollectionCreate_TakenName_ReportsTheCollectionThatHasIt()
    {
        var user = await Data.UserAsync();
        var existing = await Data.CollectionAsync(user, name: "Watch later");
        var favourites = await Data.FavouritesAsync(user);

        var taken = await Collections.CreateCoreAsync(user.ID, "WATCH LATER", VisibilityLevel.Public);
        var favouritesName = await Collections.CreateCoreAsync(user.ID, Collection.DefaultName, VisibilityLevel.Public);

        taken.Status.ShouldBe(CollectionWriteStatus.NameTaken);
        taken.ExistingCollectionID.ShouldBe(existing.ID);
        favouritesName.ExistingCollectionID.ShouldBe(favourites.ID);
    }

    [Fact]
    public async Task CollectionCreate_NewName_IsCreatedWithoutEvaluatingAchievements()
    {
        var user = await Data.UserAsync();

        var result = await Collections.CreateCoreAsync(user.ID, "Road trip", VisibilityLevel.Private);

        result.Status.ShouldBe(CollectionWriteStatus.Created);
        (await Query().Collections.SingleAsync(c => c.ID == result.Collection!.ID)).Name.ShouldBe("Road trip");
        (await Query().UserAchievements.AnyAsync(a => a.UserID == user.ID)).ShouldBeFalse();
    }

    [Fact]
    public async Task CollectionUpdate_EveryStatus()
    {
        var user = await Data.UserAsync();
        var collection = await Data.CollectionAsync(user, name: "One");
        var other = await Data.CollectionAsync(user, name: "Two");
        var favourites = await Data.FavouritesAsync(user);
        var stranger = await Data.UserAsync();

        (await Collections.UpdateCoreAsync(stranger.ID, collection.ID, "X", VisibilityLevel.Public)).Status.ShouldBe(CollectionWriteStatus.NotFound);
        (await Collections.UpdateCoreAsync(user.ID, favourites.ID, "X", VisibilityLevel.Public)).Status.ShouldBe(CollectionWriteStatus.Locked);
        (await Collections.UpdateCoreAsync(user.ID, collection.ID, " ", VisibilityLevel.Public)).Status.ShouldBe(CollectionWriteStatus.InvalidName);
        var taken = await Collections.UpdateCoreAsync(user.ID, collection.ID, "two", VisibilityLevel.Public);
        taken.Status.ShouldBe(CollectionWriteStatus.NameTaken);
        taken.ExistingCollectionID.ShouldBe(other.ID);
        (await Collections.UpdateCoreAsync(user.ID, collection.ID, "ONE", VisibilityLevel.Private)).Status.ShouldBe(CollectionWriteStatus.Updated);

        var stored = await Query().Collections.SingleAsync(c => c.ID == collection.ID);
        stored.Name.ShouldBe("ONE");
        stored.VisibilityLevel.ShouldBe(VisibilityLevel.Private);
    }

    [Fact]
    public async Task CollectionDelete_EveryStatus()
    {
        var user = await Data.UserAsync();
        var collection = await Data.CollectionAsync(user);
        var favourites = await Data.FavouritesAsync(user);

        (await Collections.DeleteCoreAsync(user.ID, favourites.ID)).Status.ShouldBe(CollectionWriteStatus.Locked);
        (await Collections.DeleteCoreAsync(user.ID, collection.ID)).Status.ShouldBe(CollectionWriteStatus.Deleted);
        (await Collections.DeleteCoreAsync(user.ID, collection.ID)).Status.ShouldBe(CollectionWriteStatus.NotFound);
    }

    [Fact]
    public async Task CollectionItems_AddAndRemoveReportWhatWasThere()
    {
        var user = await Data.UserAsync();
        var collection = await Data.CollectionAsync(user);
        var media = await Data.MediaAsync();
        var stranger = await Data.UserAsync();

        (await Collections.AddMediaCoreAsync(stranger.ID, collection.ID, media.ID)).Status.ShouldBe(CollectionWriteStatus.NotFound);
        (await Collections.AddMediaCoreAsync(user.ID, collection.ID, media.ID)).Status.ShouldBe(CollectionWriteStatus.Added);
        (await Collections.AddMediaCoreAsync(user.ID, collection.ID, media.ID)).Status.ShouldBe(CollectionWriteStatus.AlreadyPresent);
        (await Collections.RemoveMediaCoreAsync(stranger.ID, collection.ID, media.ID)).Status.ShouldBe(CollectionWriteStatus.NotFound);
        (await Collections.RemoveMediaCoreAsync(user.ID, collection.ID, media.ID)).Status.ShouldBe(CollectionWriteStatus.Removed);
        (await Collections.RemoveMediaCoreAsync(user.ID, collection.ID, media.ID)).Status.ShouldBe(CollectionWriteStatus.NotPresent);
    }

    [Fact]
    public async Task CollectionSetOrder_PutsTheListedItemsFirstAndKeepsTheRest()
    {
        var user = await Data.UserAsync();
        var a = await Data.MediaAsync();
        var b = await Data.MediaAsync();
        var c = await Data.MediaAsync();
        var d = await Data.MediaAsync();
        var gone = await Data.MediaAsync();
        var collection = await Data.CollectionAsync(user, media: [a, b, c, d]);

        // c and a were reordered on a device; "gone" was removed meanwhile, b and d stay in their current order
        var result = await Collections.SetOrderAsync(user.ID, collection.ID, [c.ID, gone.ID, a.ID]);

        result.Status.ShouldBe(CollectionWriteStatus.Reordered);
        var order = await Query().CollectionMedias
            .Where(cm => cm.CollectionID == collection.ID)
            .OrderBy(cm => cm.OrderIndex)
            .Select(cm => cm.MediaID)
            .ToListAsync();
        order.ShouldBe([c.ID, a.ID, b.ID, d.ID]);
        (await Collections.SetOrderAsync((await Data.UserAsync()).ID, collection.ID, [a.ID])).Status.ShouldBe(CollectionWriteStatus.NotFound);
    }

    /* Follows */

    [Fact]
    public async Task SetFollow_IsIdempotentAndDoesntNotify()
    {
        var user = await Data.UserAsync();
        var target = await Data.UserAsync();

        (await Followers.SetFollowAsync(user.ID, target.ID, follow: true)).ShouldBe(FollowWriteStatus.Changed);
        (await Followers.SetFollowAsync(user.ID, target.ID, follow: true)).ShouldBe(FollowWriteStatus.Unchanged);
        (await Query().Notifications.AnyAsync(n => n.RecipientUserID == target.ID)).ShouldBeFalse();
        (await Followers.SetFollowAsync(user.ID, target.ID, follow: false)).ShouldBe(FollowWriteStatus.Changed);
        (await Followers.SetFollowAsync(user.ID, target.ID, follow: false)).ShouldBe(FollowWriteStatus.Unchanged);
    }

    [Fact]
    public async Task SetFollow_YourselfOrAMissingUser()
    {
        var user = await Data.UserAsync();

        (await Followers.SetFollowAsync(user.ID, user.ID, follow: true)).ShouldBe(FollowWriteStatus.Self);
        (await Followers.SetFollowAsync(user.ID, int.MaxValue, follow: true)).ShouldBe(FollowWriteStatus.TargetMissing);
        // Unfollowing someone who doesn't exist (any more) is already done
        (await Followers.SetFollowAsync(user.ID, int.MaxValue, follow: false)).ShouldBe(FollowWriteStatus.Unchanged);
    }

    [Fact]
    public async Task RemoveFollower_ReportsWhetherTheyFollowed()
    {
        var user = await Data.UserAsync();
        var fan = await Data.UserAsync();
        await Data.FollowAsync(fan, user);

        (await Followers.RemoveFollowerCoreAsync(user.ID, fan.ID)).ShouldBe(FollowWriteStatus.Changed);
        (await Followers.RemoveFollowerCoreAsync(user.ID, fan.ID)).ShouldBe(FollowWriteStatus.Unchanged);
    }

    /* Notifications */

    [Fact]
    public async Task MarkAllSeenAndDeleteSeen_UpToAnId_LeaveNewerOnesAlone()
    {
        var user = await Data.UserAsync();
        var older = await Data.NotificationAsync(user);
        var newer = await Data.NotificationAsync(user);

        (await Notifications.MarkAllSeenAsync(user.ID, upToId: older.ID)).ShouldBe(1);
        (await Query().Notifications.SingleAsync(n => n.ID == newer.ID)).IsSeen.ShouldBeFalse();

        await Notifications.MarkAllSeenAsync(user.ID);
        (await Notifications.DeleteSeenAsync(user.ID, upToId: older.ID)).ShouldBe(1);
        (await Query().Notifications.Where(n => n.RecipientUserID == user.ID).Select(n => n.ID).ToListAsync()).ShouldBe([newer.ID]);
    }
}
