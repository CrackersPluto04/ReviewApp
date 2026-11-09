using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services.Interfaces;
using ReviewApp.Api.Tests.Support;

namespace ReviewApp.Api.Tests.Integration;

// The columns and records the offline sync builds on: rowversions that move on every write (from any client),
// Collection.UpdatedAt for item changes, and tombstones for deletions
public class SyncDataModelTests : IntegrationTest
{
    private IReviewService Reviews => Service<IReviewService>();
    private ICollectionService Collections => Service<ICollectionService>();

    private static ReviewMediaDto Request(Media media, decimal score = 7.5m, string? text = "Good") => new()
    {
        MediaDto = new MediaDto { ID = media.ExternalApiID + media.MediaType, ExternalApiID = media.ExternalApiID, MediaType = media.MediaType },
        ReviewDto = new ReviewDto { Score = score, ReviewText = text, VisibilityLevel = VisibilityLevel.Public }
    };

    private Task<Review> StoredReviewAsync(User user) => Query().Reviews.SingleAsync(r => r.UserID == user.ID);
    private Task<Collection> StoredCollectionAsync(int id) => Query().Collections.SingleAsync(c => c.ID == id);

    /* Rowversions */

    [Fact]
    public async Task Review_RowVersionIsSetOnCreateAndMovesOnEveryEdit()
    {
        var user = await Data.UserAsync();
        var media = await Data.MediaAsync();

        await Reviews.CreateReviewAsync(user.ID, Request(media));
        var created = await StoredReviewAsync(user);
        await Reviews.EditReviewAsync(user.ID, Request(media, 3));
        var edited = await StoredReviewAsync(user);

        created.RowVersion.ShouldBeGreaterThan(0);
        edited.RowVersion.ShouldBeGreaterThan(created.RowVersion);
    }

    [Fact]
    public async Task RowVersion_ReadBackAfterSave_MatchesTheDatabase()
    {
        // The SetUpdatedAt trigger updates the row again after the insert, so the value EF reads back must be the final one
        var user = await Data.UserAsync();
        var media = await Data.MediaAsync();
        using var scope = App.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ReviewApp.Api.DAL.AppDbContext>();
        var review = new Review { UserID = user.ID, MediaID = media.ID, Score = 5 };

        context.Reviews.Add(review);
        await context.SaveChangesAsync();

        review.RowVersion.ShouldBe((await StoredReviewAsync(user)).RowVersion);
    }

    [Fact]
    public async Task RowVersion_CanBeComparedInQueries()
    {
        var user = await Data.UserAsync();
        var first = await Data.ReviewAsync(user);
        var since = (await Query().Reviews.SingleAsync(r => r.ID == first.ID)).RowVersion;
        var second = await Data.ReviewAsync(user);

        var newer = await Query().Reviews
            .Where(r => r.UserID == user.ID && r.RowVersion > since)
            .Select(r => r.ID)
            .ToListAsync();

        newer.ShouldBe([second.ID]);
    }

    [Fact]
    public async Task ParallelWebEditsOfOneReview_BothSucceed()
    {
        // RowVersion is no concurrency token: the web keeps "last write wins" instead of failing one of the edits
        var user = await Data.UserAsync();
        var media = await Data.MediaAsync();
        await Reviews.CreateReviewAsync(user.ID, Request(media));

        var results = await Task.WhenAll(
            Reviews.EditReviewAsync(user.ID, Request(media, 2, "first")),
            Reviews.EditReviewAsync(user.ID, Request(media, 9, "second")));

        results.ShouldAllBe(r => r.Success);
    }

    /* Collections */

    [Fact]
    public async Task Collection_EveryChangeOfItsItemsMovesItsVersion()
    {
        var user = await Data.UserAsync();
        var first = await Data.MediaAsync();
        var second = await Data.MediaAsync();
        var collection = await Data.CollectionAsync(user, media: first);
        var versions = new List<(long Version, DateTime UpdatedAt)>();
        async Task RecordAsync()
        {
            var stored = await StoredCollectionAsync(collection.ID);
            versions.Add((stored.RowVersion, stored.UpdatedAt));
        }

        await RecordAsync();
        await Task.Delay(10);
        (await Collections.AddMediaToCollectionAsync(user.ID, collection.ID, MediaType.Movie, second.ExternalApiID)).ShouldBeTrue();
        await RecordAsync();
        await Task.Delay(10);
        (await Collections.ReorderMediaAsync(user.ID, collection.ID, second.ID, 0)).ShouldBeTrue();
        await RecordAsync();
        await Task.Delay(10);
        (await Collections.RemoveMediaFromCollectionAsync(user.ID, collection.ID, MediaType.Movie, first.ExternalApiID)).ShouldBeTrue();
        await RecordAsync();
        await Task.Delay(10);
        (await Collections.UpdateCollectionAsync(user.ID, new UpdateCollectionDto { CollectionID = collection.ID, Name = TestData.Unique("Renamed "), VisibilityLevel = VisibilityLevel.Private })).Success.ShouldBeTrue();
        await RecordAsync();

        for (var i = 1; i < versions.Count; i++)
        {
            versions[i].Version.ShouldBeGreaterThan(versions[i - 1].Version);
            versions[i].UpdatedAt.ShouldBeGreaterThan(versions[i - 1].UpdatedAt);
        }
    }

    [Fact]
    public async Task Favourites_FromTheTrigger_HasAVersionAndUpdatedAt()
    {
        var user = await Data.UserAsync();

        var favourites = await Query().Collections.SingleAsync(c => c.UserID == user.ID && c.Name == Collection.DefaultName);

        favourites.RowVersion.ShouldBeGreaterThan(0);
        favourites.UpdatedAt.ShouldBe(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    /* Tombstones */

    [Fact]
    public async Task DeletingAReview_LeavesATombstoneForItsOwner()
    {
        var user = await Data.UserAsync();
        var review = await Data.ReviewAsync(user);
        var before = (await Query().Reviews.SingleAsync(r => r.ID == review.ID)).RowVersion;

        (await Reviews.DeleteReviewAsync(user.ID, review.ID)).Success.ShouldBeTrue();

        var tombstone = await Query().SyncTombstones.SingleAsync(t => t.UserID == user.ID);
        tombstone.EntityType.ShouldBe(SyncEntityType.Review);
        tombstone.EntityID.ShouldBe(review.ID);
        tombstone.RowVersion.ShouldBeGreaterThan(before);
        tombstone.DeletedAt.ShouldBe(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task DeletingACollection_LeavesATombstone_ButAFailedDeleteDoesNot()
    {
        var user = await Data.UserAsync();
        var stranger = await Data.UserAsync();
        var collection = await Data.CollectionAsync(user);
        var favourites = await Data.FavouritesAsync(user);

        (await Collections.DeleteCollectionAsync(user.ID, favourites.ID)).Success.ShouldBeFalse();
        (await Collections.DeleteCollectionAsync(stranger.ID, collection.ID)).Success.ShouldBeFalse();
        (await Collections.DeleteCollectionAsync(user.ID, collection.ID)).Success.ShouldBeTrue();

        var tombstone = await Query().SyncTombstones.SingleAsync(t => t.UserID == user.ID);
        tombstone.EntityType.ShouldBe(SyncEntityType.Collection);
        tombstone.EntityID.ShouldBe(collection.ID);
    }
}
