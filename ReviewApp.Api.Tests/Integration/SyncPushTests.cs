using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services.Interfaces;
using ReviewApp.Api.Services.Sync;
using ReviewApp.Api.Tests.Support;

namespace ReviewApp.Api.Tests.Integration;

// SyncService.PushAsync: the offline changes of the Android app, applied one by one with exactly-once delivery,
// three-way merges, conflicts that change nothing, and the side effects after each commit
public class SyncPushTests : IntegrationTest
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static SyncOpDto Op(string type, object payload, string? clientRef = null, Guid? opId = null, params Guid[] dependsOn) => new()
    {
        OpId = opId ?? Guid.NewGuid(),
        Type = type,
        ClientRef = clientRef,
        DependsOn = dependsOn.Length > 0 ? [.. dependsOn] : null,
        Payload = JsonSerializer.SerializeToElement(payload, JsonOptions)
    };

    // Every call is its own scope, like a separate request
    private Task<SyncPushResponse> PushAsync(User user, params SyncOpDto[] ops) => Service<ISyncService>().PushAsync(user.ID, ops);

    private async Task<SyncOpResult> PushOneAsync(User user, SyncOpDto op) => (await PushAsync(user, op)).Results.Single();

    private static object MediaKey(Media media) => new { type = (int)media.MediaType, externalApiId = media.ExternalApiID };

    private static object Fields(decimal score = 8m, string? text = "Offline text", VisibilityLevel visibility = VisibilityLevel.Public) =>
        new { score, reviewText = text, visibility = (int)visibility };

    private static object ById(int id) => new { id };
    private static object ByRef(string clientRef) => new { @ref = clientRef };

    private static long Version(SyncOpResult result) => result.Entity!.Value.GetProperty("version").GetInt64();

    private async Task<Review> StoredReviewAsync(int id) => await Query().Reviews.AsNoTracking().SingleAsync(r => r.ID == id);

    // Like an edit on the web: a plain UPDATE, the trigger and the rowversion move as usual
    private async Task WebEditAsync(Review review, string? text = null, decimal? score = null) =>
        await Query().Reviews.Where(r => r.ID == review.ID).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.ReviewText, r => text ?? r.ReviewText)
            .SetProperty(r => r.Score, r => score ?? r.Score));

    private async Task<long> VersionOfAsync(Review review) => (await StoredReviewAsync(review.ID)).RowVersion;

    private async Task<bool> RecordedAsync(Guid opId) => await Query().SyncOperations.AnyAsync(s => s.OperationID == opId);

    /* Exactly once */

    [Fact]
    public async Task ReviewCreate_IsAppliedWithTheWrittenTime_AndUnlocksAchievements()
    {
        var user = await Data.UserAsync();
        var media = await Data.MediaAsync();
        var writtenAt = DateTime.UtcNow.AddDays(-2);

        var response = await PushAsync(user, Op(SyncOpTypes.ReviewCreate,
            new { media = MediaKey(media), fields = Fields(), createdAt = new DateTimeOffset(writtenAt) }, clientRef: "r1"));

        var result = response.Results.Single();
        result.Status.ShouldBe(SyncStatus.Applied);
        var stored = await StoredReviewAsync(result.EntityId!.Value);
        stored.UserID.ShouldBe(user.ID);
        stored.ReviewText.ShouldBe("Offline text");
        stored.CreatedAt.ShouldBe(writtenAt, TimeSpan.FromSeconds(1));
        Version(result).ShouldBe(stored.RowVersion);
        result.Entity!.Value.GetProperty("media").GetProperty("externalApiId").GetString().ShouldBe(media.ExternalApiID);
        // The first movie review: Movie Master bronze, in the body (the device shows one summary)
        response.Unlocked.ShouldContain(u => u.GroupCode == "MOVIE_MASTER");
    }

    [Fact]
    public async Task SameOpSentAgain_IsAppliedOnce_AndAnsweredTheSame()
    {
        var user = await Data.UserAsync();
        var media = await Data.MediaAsync();
        var op = Op(SyncOpTypes.ReviewCreate, new { media = MediaKey(media), fields = Fields() });

        var first = await PushOneAsync(user, op);
        // The answer was lost, the device sends it again
        var again = await PushOneAsync(user, op);

        again.Status.ShouldBe(SyncStatus.Applied);
        again.EntityId.ShouldBe(first.EntityId);
        (await Query().Reviews.CountAsync(r => r.UserID == user.ID)).ShouldBe(1);
    }

    [Fact]
    public async Task SameOpInTwoParallelRequests_IsAppliedOnce()
    {
        // Without the claimed OperationID the second one would see the name as taken
        var user = await Data.UserAsync();
        var op = Op(SyncOpTypes.CollectionCreate, new { fields = new { name = "Parallel", visibility = 0 } });

        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => PushOneAsync(user, op)));

        results.ShouldAllBe(r => r.Status == SyncStatus.Applied);
        results.Select(r => r.EntityId).Distinct().Count().ShouldBe(1);
        (await Query().Collections.CountAsync(c => c.UserID == user.ID && c.Name == "Parallel")).ShouldBe(1);
    }

    [Fact]
    public async Task ClientRef_ResolvesInTheSameAndInALaterRequest()
    {
        var user = await Data.UserAsync();
        var first = await Data.MediaAsync();
        var second = await Data.MediaAsync();
        var create = Op(SyncOpTypes.CollectionCreate, new { fields = new { name = "Made offline", visibility = 0 } }, clientRef: "c1");

        await PushAsync(user, create,
            Op(SyncOpTypes.CollectionAddItem, new { collection = ByRef("c1"), media = MediaKey(first) }, dependsOn: create.OpId!.Value));
        var later = await PushOneAsync(user,
            Op(SyncOpTypes.CollectionAddItem, new { collection = ByRef("c1"), media = MediaKey(second) }, dependsOn: create.OpId!.Value));

        later.Status.ShouldBe(SyncStatus.Applied);
        var collection = await Query().Collections.Include(c => c.CollectionMedias).SingleAsync(c => c.UserID == user.ID && c.Name == "Made offline");
        collection.CollectionMedias.Select(cm => cm.MediaID).Order().ShouldBe(new[] { first.ID, second.ID }.Order());
        later.Entity!.Value.GetProperty("items").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task UnknownClientRef_IsSkipped()
    {
        var user = await Data.UserAsync();
        var media = await Data.MediaAsync();

        (await PushOneAsync(user, Op(SyncOpTypes.CollectionAddItem, new { collection = ByRef("never-created"), media = MediaKey(media) })))
            .ShouldSatisfyAllConditions(r => r.Status.ShouldBe(SyncStatus.Skipped), r => r.Reason.ShouldBe(SyncReason.Dependency));
    }

    [Fact]
    public async Task OpsDependingOnAFailedOne_AreSkipped_TheRestContinue()
    {
        var user = await Data.UserAsync();
        await Data.CollectionAsync(user, name: "Taken");
        var media = await Data.MediaAsync();
        var create = Op(SyncOpTypes.CollectionCreate, new { fields = new { name = "Taken", visibility = 0 } }, clientRef: "c1");
        var add = Op(SyncOpTypes.CollectionAddItem, new { collection = ByRef("c1"), media = MediaKey(media) }, dependsOn: create.OpId!.Value);
        var unrelated = Op(SyncOpTypes.ProfileUpdate, new { fields = new { bio = "Still applied" } });

        var results = (await PushAsync(user, create, add, unrelated)).Results;

        results.Select(r => r.Status).ShouldBe([SyncStatus.Conflict, SyncStatus.Skipped, SyncStatus.Applied]);
        (await RecordedAsync(create.OpId!.Value)).ShouldBeFalse();
    }

    [Fact]
    public async Task DependencyFromAnEarlierRequest_CountsWhenItWasApplied()
    {
        var user = await Data.UserAsync();
        var applied = Op(SyncOpTypes.ProfileUpdate, new { fields = new { bio = "One" } });
        await PushOneAsync(user, applied);

        (await PushOneAsync(user, Op(SyncOpTypes.ProfileUpdate, new { fields = new { bio = "Two" } }, dependsOn: applied.OpId!.Value)))
            .Status.ShouldBe(SyncStatus.Applied);
        (await PushOneAsync(user, Op(SyncOpTypes.ProfileUpdate, new { fields = new { bio = "Three" } }, dependsOn: Guid.NewGuid())))
            .Status.ShouldBe(SyncStatus.Skipped);
    }

    [Fact]
    public async Task InvalidOrUnknownOps_AreRejected_TheRestContinue()
    {
        var user = await Data.UserAsync();

        var results = (await PushAsync(user,
            Op("review.rate", new { }),
            Op(SyncOpTypes.FollowSet, new { userId = 0, follow = true }),
            Op(SyncOpTypes.ProfileUpdate, new { fields = new { bio = "Fine" } }))).Results;

        results.Select(r => (r.Status, r.Reason)).ShouldBe([
            (SyncStatus.Rejected, SyncReason.UnknownType),
            (SyncStatus.Rejected, SyncReason.Invalid),
            (SyncStatus.Applied, null)]);
    }

    /* Reviews */

    [Fact]
    public async Task ReviewUpdate_NobodyElseChangedIt_IsApplied()
    {
        var user = await Data.UserAsync();
        var review = await Data.ReviewAsync(user, text: "Before");

        var result = await PushOneAsync(user, Op(SyncOpTypes.ReviewUpdate, new
        {
            review = ById(review.ID),
            baseVersion = await VersionOfAsync(review),
            @base = Fields(8m, "Before"),
            fields = Fields(6m, "After")
        }));

        result.Status.ShouldBe(SyncStatus.Applied);
        var stored = await StoredReviewAsync(review.ID);
        (stored.Score, stored.ReviewText).ShouldBe((6m, "After"));
        Version(result).ShouldBe(stored.RowVersion);
    }

    [Fact]
    public async Task ReviewUpdate_OtherFieldEditedOnTheWeb_IsMerged()
    {
        var user = await Data.UserAsync();
        var review = await Data.ReviewAsync(user, score: 8m, text: "Before");
        var baseVersion = await VersionOfAsync(review);
        await WebEditAsync(review, text: "Edited on the web");

        var result = await PushOneAsync(user, Op(SyncOpTypes.ReviewUpdate, new
        {
            review = ById(review.ID),
            baseVersion,
            @base = Fields(8m, "Before"),
            fields = Fields(3m, "Before")
        }));

        result.Status.ShouldBe(SyncStatus.Merged);
        var stored = await StoredReviewAsync(review.ID);
        (stored.Score, stored.ReviewText).ShouldBe((3m, "Edited on the web"));
        result.Entity!.Value.GetProperty("reviewText").GetString().ShouldBe("Edited on the web");
    }

    [Fact]
    public async Task ReviewUpdate_SameFieldEditedOnTheWeb_IsAConflict_AndChangesNothing()
    {
        var user = await Data.UserAsync();
        var review = await Data.ReviewAsync(user, score: 8m, text: "Before");
        var baseVersion = await VersionOfAsync(review);
        await WebEditAsync(review, text: "Theirs");
        var op = Op(SyncOpTypes.ReviewUpdate, new
        {
            review = ById(review.ID),
            baseVersion,
            @base = Fields(8m, "Before"),
            fields = Fields(2m, "Mine")
        });

        var result = await PushOneAsync(user, op);

        result.Status.ShouldBe(SyncStatus.Conflict);
        result.Reason.ShouldBe(SyncReason.FieldConflict);
        result.ConflictFields.ShouldBe(["reviewText"]);
        result.Entity!.Value.GetProperty("reviewText").GetString().ShouldBe("Theirs");
        // Not even the score that had no conflict: the user decides on the whole edit
        var stored = await StoredReviewAsync(review.ID);
        (stored.Score, stored.ReviewText).ShouldBe((8m, "Theirs"));
        (await RecordedAsync(op.OpId!.Value)).ShouldBeFalse();
    }

    [Fact]
    public async Task ReviewUpdate_ResentWithTheServerVersionAsBase_ResolvesTheConflict()
    {
        // "Keep mine": the device sends the edit again, based on the version the conflict showed
        var user = await Data.UserAsync();
        var review = await Data.ReviewAsync(user, text: "Before");
        var baseVersion = await VersionOfAsync(review);
        await WebEditAsync(review, text: "Theirs");
        var conflict = await PushOneAsync(user, Op(SyncOpTypes.ReviewUpdate,
            new { review = ById(review.ID), baseVersion, @base = Fields(8m, "Before"), fields = Fields(8m, "Mine") }));

        var resolved = await PushOneAsync(user, Op(SyncOpTypes.ReviewUpdate,
            new { review = ById(review.ID), baseVersion = Version(conflict), @base = Fields(8m, "Theirs"), fields = Fields(8m, "Mine") }));

        resolved.Status.ShouldBe(SyncStatus.Applied);
        (await StoredReviewAsync(review.ID)).ReviewText.ShouldBe("Mine");
    }

    [Fact]
    public async Task ReviewUpdate_OfADeletedReview_IsAConflict()
    {
        var user = await Data.UserAsync();
        var review = await Data.ReviewAsync(user);
        await Service<IReviewService>().DeleteReviewAsync(user.ID, review.ID);

        var result = await PushOneAsync(user, Op(SyncOpTypes.ReviewUpdate, new { review = ById(review.ID), fields = Fields() }));

        (result.Status, result.Reason).ShouldBe((SyncStatus.Conflict, SyncReason.Deleted));
    }

    [Fact]
    public async Task ReviewCreate_AlreadyReviewedOnTheWeb_IsAConflictShowingThatReview()
    {
        var user = await Data.UserAsync();
        var media = await Data.MediaAsync();
        var existing = await Data.ReviewAsync(user, media, text: "From the web");

        var result = await PushOneAsync(user, Op(SyncOpTypes.ReviewCreate, new { media = MediaKey(media), fields = Fields() }));

        (result.Status, result.Reason, result.EntityId).ShouldBe((SyncStatus.Conflict, SyncReason.AlreadyReviewed, existing.ID));
        result.Entity!.Value.GetProperty("reviewText").GetString().ShouldBe("From the web");
        (await StoredReviewAsync(existing.ID)).ReviewText.ShouldBe("From the web");
    }

    [Fact]
    public async Task ReviewDelete_Unchanged_DeletesIt_AlreadyGone_IsApplied()
    {
        var user = await Data.UserAsync();
        var review = await Data.ReviewAsync(user);
        var delete = new { review = ById(review.ID), baseVersion = await VersionOfAsync(review) };

        (await PushOneAsync(user, Op(SyncOpTypes.ReviewDelete, delete))).Status.ShouldBe(SyncStatus.Applied);
        // A second device deleted it too
        (await PushOneAsync(user, Op(SyncOpTypes.ReviewDelete, delete))).Status.ShouldBe(SyncStatus.Applied);

        (await Query().Reviews.AnyAsync(r => r.ID == review.ID)).ShouldBeFalse();
        (await Query().SyncTombstones.CountAsync(t => t.EntityType == SyncEntityType.Review && t.EntityID == review.ID)).ShouldBe(1);
    }

    [Fact]
    public async Task ReviewDelete_EditedSince_IsAConflict()
    {
        var user = await Data.UserAsync();
        var review = await Data.ReviewAsync(user);
        var baseVersion = await VersionOfAsync(review);
        await WebEditAsync(review, text: "Edited after the delete");

        var result = await PushOneAsync(user, Op(SyncOpTypes.ReviewDelete, new { review = ById(review.ID), baseVersion }));

        (result.Status, result.Reason).ShouldBe((SyncStatus.Conflict, SyncReason.ModifiedSince));
        (await Query().Reviews.AnyAsync(r => r.ID == review.ID)).ShouldBeTrue();
    }

    [Fact]
    public async Task ReviewCreateEditDelete_ByClientRef_InOneRequest()
    {
        var user = await Data.UserAsync();
        var media = await Data.MediaAsync();
        var create = Op(SyncOpTypes.ReviewCreate, new { media = MediaKey(media), fields = Fields() }, clientRef: "r1");
        var update = Op(SyncOpTypes.ReviewUpdate, new { review = ByRef("r1"), fields = Fields(2m) }, dependsOn: create.OpId!.Value);
        var delete = Op(SyncOpTypes.ReviewDelete, new { review = ByRef("r1") }, dependsOn: update.OpId!.Value);

        var results = (await PushAsync(user, create, update, delete)).Results;

        results.ShouldAllBe(r => r.Status == SyncStatus.Applied);
        (await Query().Reviews.AnyAsync(r => r.UserID == user.ID)).ShouldBeFalse();
    }

    /* Media resolution */

    [Fact]
    public async Task UnknownMedia_IsRejected_OutageIsRetried_NeitherStoresAMediaRow()
    {
        var user = await Data.UserAsync();
        var unknown = TestData.Unique("7");
        var unavailable = TestData.Unique("7");
        App.Tmdb.MarkUnknown(unknown);
        App.Tmdb.FailById(unavailable);

        var results = (await PushAsync(user,
            Op(SyncOpTypes.ReviewCreate, new { media = new { type = 0, externalApiId = unknown }, fields = Fields() }),
            Op(SyncOpTypes.ReviewCreate, new { media = new { type = 0, externalApiId = unavailable }, fields = Fields() }))).Results;

        results.Select(r => (r.Status, r.Reason)).ShouldBe([
            (SyncStatus.Rejected, SyncReason.MediaNotFound),
            (SyncStatus.Retry, SyncReason.SourceUnavailable)]);
        (await Query().Media.AnyAsync(m => m.ExternalApiID == unknown || m.ExternalApiID == unavailable)).ShouldBeFalse();
    }

    [Fact]
    public async Task NewMediaLookups_AreCappedPerRequest_TheRestAreDeferred()
    {
        var user = await Data.UserAsync();
        var collection = await Data.CollectionAsync(user);
        var ops = Enumerable.Range(0, SyncService.MaxNewMediaLookups + 2)
            .Select(_ => Op(SyncOpTypes.CollectionAddItem, new
            {
                collection = ById(collection.ID),
                media = new { type = 0, externalApiId = TestData.Unique("7") }
            }))
            .ToArray();

        var results = (await PushAsync(user, ops)).Results;

        results.Count(r => r.Status == SyncStatus.Applied).ShouldBe(SyncService.MaxNewMediaLookups);
        results.Skip(SyncService.MaxNewMediaLookups).ShouldAllBe(r => r.Status == SyncStatus.Retry && r.Reason == SyncReason.Deferred);
    }

    /* Collections */

    [Fact]
    public async Task CollectionAddItem_ToADeletedCollection_IsAConflict_WithoutLookingUpTheMedia()
    {
        var user = await Data.UserAsync();
        var collection = await Data.CollectionAsync(user);
        await Service<ICollectionService>().DeleteCollectionAsync(user.ID, collection.ID);
        var externalId = TestData.Unique("7");

        var result = await PushOneAsync(user, Op(SyncOpTypes.CollectionAddItem,
            new { collection = ById(collection.ID), media = new { type = 0, externalApiId = externalId } }));

        (result.Status, result.Reason).ShouldBe((SyncStatus.Conflict, SyncReason.CollectionDeleted));
        App.Tmdb.LookupsOf(externalId).ShouldBe(0);
    }

    [Fact]
    public async Task CollectionItems_AddingAPresentOne_OrRemovingAMissingOne_IsApplied()
    {
        var user = await Data.UserAsync();
        var present = await Data.MediaAsync();
        var missing = await Data.MediaAsync();
        var collection = await Data.CollectionAsync(user, media: present);

        var results = (await PushAsync(user,
            Op(SyncOpTypes.CollectionAddItem, new { collection = ById(collection.ID), media = MediaKey(present) }),
            Op(SyncOpTypes.CollectionRemoveItem, new { collection = ById(collection.ID), media = MediaKey(missing) }),
            Op(SyncOpTypes.CollectionRemoveItem, new { collection = ById(collection.ID), media = new { type = 0, externalApiId = TestData.Unique("7") } }))).Results;

        results.ShouldAllBe(r => r.Status == SyncStatus.Applied);
        (await Query().CollectionMedias.CountAsync(cm => cm.CollectionID == collection.ID)).ShouldBe(1);
    }

    [Fact]
    public async Task CollectionCreate_NameTaken_IsAConflictNamingTheExistingCollection()
    {
        var user = await Data.UserAsync();
        var existing = await Data.CollectionAsync(user, name: "Weekend");

        var result = await PushOneAsync(user, Op(SyncOpTypes.CollectionCreate, new { fields = new { name = "weekend", visibility = 0 } }));

        (result.Status, result.Reason, result.EntityId).ShouldBe((SyncStatus.Conflict, SyncReason.NameTaken, existing.ID));
        result.Entity!.Value.GetProperty("name").GetString().ShouldBe("Weekend");
    }

    [Fact]
    public async Task CollectionUpdate_RenamedOnTheWebWhileVisibilityChangedOffline_IsMerged()
    {
        var user = await Data.UserAsync();
        var collection = await Data.CollectionAsync(user, VisibilityLevel.Private, name: "Before");
        var baseVersion = (await Query().Collections.SingleAsync(c => c.ID == collection.ID)).RowVersion;
        await Service<ICollectionService>().UpdateCollectionAsync(user.ID,
            new UpdateCollectionDto { CollectionID = collection.ID, Name = "Renamed on the web", VisibilityLevel = VisibilityLevel.Private });

        var result = await PushOneAsync(user, Op(SyncOpTypes.CollectionUpdate, new
        {
            collection = ById(collection.ID),
            baseVersion,
            @base = new { name = "Before", visibility = 0 },
            fields = new { name = "Before", visibility = 1 }
        }));

        result.Status.ShouldBe(SyncStatus.Merged);
        var stored = await Query().Collections.SingleAsync(c => c.ID == collection.ID);
        (stored.Name, stored.VisibilityLevel).ShouldBe(("Renamed on the web", VisibilityLevel.Public));
    }

    [Fact]
    public async Task CollectionUpdate_ItemAddedElsewhere_IsNoConflict()
    {
        // An item change moves the collection's version, but no field changed
        var user = await Data.UserAsync();
        var collection = await Data.CollectionAsync(user, name: "Before");
        var baseVersion = (await Query().Collections.SingleAsync(c => c.ID == collection.ID)).RowVersion;
        var media = await Data.MediaAsync();
        await Service<ICollectionService>().AddMediaToCollectionAsync(user.ID, collection.ID, media.MediaType, media.ExternalApiID);

        var result = await PushOneAsync(user, Op(SyncOpTypes.CollectionUpdate, new
        {
            collection = ById(collection.ID),
            baseVersion,
            @base = new { name = "Before", visibility = 1 },
            fields = new { name = "After", visibility = 1 }
        }));

        result.Status.ShouldBe(SyncStatus.Applied);
        (await Query().Collections.SingleAsync(c => c.ID == collection.ID)).Name.ShouldBe("After");
    }

    [Fact]
    public async Task Favourites_CantBeEditedOrDeleted()
    {
        var user = await Data.UserAsync();
        var favourites = await Data.FavouritesAsync(user);

        var results = (await PushAsync(user,
            Op(SyncOpTypes.CollectionUpdate, new { collection = ById(favourites.ID), fields = new { name = "Mine", visibility = 1 } }),
            Op(SyncOpTypes.CollectionDelete, new { collection = ById(favourites.ID) }))).Results;

        results.ShouldAllBe(r => r.Status == SyncStatus.Rejected && r.Reason == SyncReason.Locked);
        (await Query().Collections.AnyAsync(c => c.ID == favourites.ID && c.Name == Collection.DefaultName)).ShouldBeTrue();
    }

    [Fact]
    public async Task CollectionReorder_AppliesTheDevicesOrder_KeepingItemsAddedElsewhere()
    {
        var user = await Data.UserAsync();
        var a = await Data.MediaAsync();
        var b = await Data.MediaAsync();
        var c = await Data.MediaAsync();
        var addedElsewhere = await Data.MediaAsync();
        var collection = await Data.CollectionAsync(user, media: [a, b, c, addedElsewhere]);

        var result = await PushOneAsync(user, Op(SyncOpTypes.CollectionReorder,
            new { collection = ById(collection.ID), items = new[] { MediaKey(c), MediaKey(a), MediaKey(b) } }));

        result.Status.ShouldBe(SyncStatus.Applied);
        var order = await Query().CollectionMedias.Where(cm => cm.CollectionID == collection.ID)
            .OrderBy(cm => cm.OrderIndex).Select(cm => cm.MediaID).ToListAsync();
        order.ShouldBe([c.ID, a.ID, b.ID, addedElsewhere.ID]);
    }

    [Fact]
    public async Task CollectionDelete_RemovesIt_AndAGoneOneIsApplied()
    {
        var user = await Data.UserAsync();
        var collection = await Data.CollectionAsync(user);
        var delete = new { collection = ById(collection.ID) };

        (await PushOneAsync(user, Op(SyncOpTypes.CollectionDelete, delete))).Status.ShouldBe(SyncStatus.Applied);
        (await PushOneAsync(user, Op(SyncOpTypes.CollectionDelete, delete))).Status.ShouldBe(SyncStatus.Applied);

        (await Query().Collections.AnyAsync(c => c.ID == collection.ID)).ShouldBeFalse();
    }

    /* Profile, follows, notifications */

    [Fact]
    public async Task Profile_LatestWins_AndReportsTheOverwrittenValue()
    {
        var user = await Data.UserAsync(u => u.Bio = "Edited on the web");

        var result = await PushOneAsync(user, Op(SyncOpTypes.ProfileUpdate,
            new { fields = new { bio = "From the phone" }, @base = new { bio = "What the phone saw" } }));

        result.Status.ShouldBe(SyncStatus.Applied);
        result.Previous.ShouldBe(new Dictionary<string, string?> { ["bio"] = "Edited on the web" });
        (await Query().Users.SingleAsync(u => u.ID == user.ID)).Bio.ShouldBe("From the phone");
    }

    [Fact]
    public async Task Profile_EmptyClears_LeftOutKeeps()
    {
        var user = await Data.UserAsync(u => { u.Bio = "Old"; u.ProfilePictureUrl = "https://img.test/me.png"; });

        var result = await PushOneAsync(user, Op(SyncOpTypes.ProfileUpdate, new { fields = new { bio = "" } }));

        result.Previous.ShouldBeNull();
        var stored = await Query().Users.SingleAsync(u => u.ID == user.ID);
        (stored.Bio, stored.ProfilePictureUrl).ShouldBe((null, "https://img.test/me.png"));
    }

    [Fact]
    public async Task Follow_IsIdempotent_AndNotifiesOnce()
    {
        var user = await Data.UserAsync();
        var target = await Data.UserAsync();
        var follow = new { userId = target.ID, follow = true };

        var results = (await PushAsync(user, Op(SyncOpTypes.FollowSet, follow), Op(SyncOpTypes.FollowSet, follow))).Results;

        results.ShouldAllBe(r => r.Status == SyncStatus.Applied);
        (await Query().UserFollowers.CountAsync(f => f.FollowerID == user.ID && f.FollowingID == target.ID)).ShouldBe(1);
        (await Query().Notifications.CountAsync(n => n.RecipientUserID == target.ID && n.Type == NotificationType.NewFollower)).ShouldBe(1);
    }

    [Fact]
    public async Task Unfollow_NotFollowing_IsApplied_FollowingYourselfOrNobodyIsRejected()
    {
        var user = await Data.UserAsync();
        var other = await Data.UserAsync();

        var results = (await PushAsync(user,
            Op(SyncOpTypes.FollowSet, new { userId = other.ID, follow = false }),
            Op(SyncOpTypes.FollowSet, new { userId = user.ID, follow = true }),
            Op(SyncOpTypes.FollowSet, new { userId = int.MaxValue, follow = true }))).Results;

        results.Select(r => (r.Status, r.Reason)).ShouldBe([
            (SyncStatus.Applied, null),
            (SyncStatus.Rejected, SyncReason.Self),
            (SyncStatus.Rejected, SyncReason.UserNotFound)]);
    }

    [Fact]
    public async Task Notifications_MarkAllSeenAndDeleteSeen_StopAtTheNewestTheDeviceHad()
    {
        var user = await Data.UserAsync();
        var notifications = await Data.NotificationsAsync(user, 3);
        var seenOnDevice = notifications[1].ID;

        await PushAsync(user, Op(SyncOpTypes.NotificationMarkAllSeen, new { upToId = seenOnDevice }));

        var unseen = await Query().Notifications.Where(n => n.RecipientUserID == user.ID && !n.IsSeen).Select(n => n.ID).ToListAsync();
        unseen.ShouldBe([notifications[2].ID]);

        await PushAsync(user, Op(SyncOpTypes.NotificationDeleteSeen, new { upToId = seenOnDevice }));
        (await Query().Notifications.CountAsync(n => n.RecipientUserID == user.ID)).ShouldBe(1);
    }

    /* Ownership */

    [Fact]
    public async Task AnotherUsersContent_IsNeverChanged()
    {
        var user = await Data.UserAsync();
        var owner = await Data.UserAsync();
        var review = await Data.ReviewAsync(owner, text: "Owner's");
        var collection = await Data.CollectionAsync(owner, name: "Owner's");
        var notification = await Data.NotificationAsync(owner);
        var media = await Data.MediaAsync();

        var results = (await PushAsync(user,
            Op(SyncOpTypes.ReviewUpdate, new { review = ById(review.ID), fields = Fields(1m, "Hijacked") }),
            Op(SyncOpTypes.ReviewDelete, new { review = ById(review.ID) }),
            Op(SyncOpTypes.CollectionUpdate, new { collection = ById(collection.ID), fields = new { name = "Hijacked", visibility = 1 } }),
            Op(SyncOpTypes.CollectionAddItem, new { collection = ById(collection.ID), media = MediaKey(media) }),
            Op(SyncOpTypes.CollectionDelete, new { collection = ById(collection.ID) }),
            Op(SyncOpTypes.NotificationDelete, new { id = notification.ID }))).Results;

        results.ShouldNotContain(r => r.Status == SyncStatus.Merged);
        (await StoredReviewAsync(review.ID)).ReviewText.ShouldBe("Owner's");
        var storedCollection = await Query().Collections.Include(c => c.CollectionMedias).SingleAsync(c => c.ID == collection.ID);
        storedCollection.Name.ShouldBe("Owner's");
        storedCollection.CollectionMedias.ShouldBeEmpty();
        (await Query().Notifications.AnyAsync(n => n.ID == notification.ID)).ShouldBeTrue();
    }

    [Fact]
    public async Task AnotherUsersClientRef_DoesNotResolve()
    {
        var user = await Data.UserAsync();
        var other = await Data.UserAsync();
        await PushOneAsync(other, Op(SyncOpTypes.CollectionCreate, new { fields = new { name = "Theirs", visibility = 0 } }, clientRef: "shared-ref"));
        var media = await Data.MediaAsync();

        (await PushOneAsync(user, Op(SyncOpTypes.CollectionAddItem, new { collection = ByRef("shared-ref"), media = MediaKey(media) })))
            .Status.ShouldBe(SyncStatus.Skipped);
    }
}
