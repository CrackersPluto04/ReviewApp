using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services.Interfaces;
using ReviewApp.Api.Tests.Support;

namespace ReviewApp.Api.Tests.Integration;

public class CollectionServiceTests : IntegrationTest
{
    private ICollectionService Collections => Service<ICollectionService>();

    private Task<List<int>> OrderAsync(Collection collection) =>
        Query().CollectionMedias.Where(cm => cm.CollectionID == collection.ID).OrderBy(cm => cm.OrderIndex).Select(cm => cm.MediaID).ToListAsync();

    /* Favourites (created by the AddDefaultCollection trigger) */

    [Fact]
    public async Task NewUser_GetsAPrivateFavouritesCollection()
    {
        var user = await Data.UserAsync();

        var favourites = await Data.FavouritesAsync(user);

        favourites.VisibilityLevel.ShouldBe(VisibilityLevel.Private);
        var listed = (await Collections.GetUserCollectionsAsync(user.Username, user.ID))!.Single();
        listed.IsDefault.ShouldBeTrue();
        listed.Name.ShouldBe(Collection.DefaultName);
    }

    [Fact]
    public async Task Favourites_CantBeRenamedEditedOrDeleted()
    {
        var user = await Data.UserAsync();
        var favourites = await Data.FavouritesAsync(user);

        var (renamed, renameNotFound, message, _) = await Collections.UpdateCollectionAsync(user.ID,
            new UpdateCollectionDto { CollectionID = favourites.ID, Name = "Best", VisibilityLevel = VisibilityLevel.Public });
        var (deleted, deleteNotFound, _) = await Collections.DeleteCollectionAsync(user.ID, favourites.ID);

        renamed.ShouldBeFalse();
        renameNotFound.ShouldBeFalse();
        message.ShouldBe("Your Favourites collection can't be renamed, edited or deleted.");
        deleted.ShouldBeFalse();
        deleteNotFound.ShouldBeFalse();
        (await Query().Collections.SingleAsync(c => c.ID == favourites.ID)).VisibilityLevel.ShouldBe(VisibilityLevel.Private);
    }

    [Theory]
    [InlineData("Favourites")]
    [InlineData("FAVOURITES")]
    public async Task NoOtherCollection_CanTakeTheFavouritesName(string name)
    {
        var user = await Data.UserAsync();
        var other = await Data.CollectionAsync(user);

        (await Collections.CreateCollectionAsync(user.ID, new CreateCollectionDto { Name = name })).ShouldBeNull();
        (await Collections.UpdateCollectionAsync(user.ID, new UpdateCollectionDto { CollectionID = other.ID, Name = name })).Success.ShouldBeFalse();
    }

    /* Create / update / delete */

    [Fact]
    public async Task Create_ReturnsTheNewCollection()
    {
        var user = await Data.UserAsync();

        var created = await Collections.CreateCollectionAsync(user.ID, new CreateCollectionDto { Name = "Watch later", VisibilityLevel = VisibilityLevel.FollowersOnly });

        created!.Name.ShouldBe("Watch later");
        created.VisibilityLevel.ShouldBe(VisibilityLevel.FollowersOnly);
        created.IsOwner.ShouldBeTrue();
        created.MediaCount.ShouldBe(0);
        (await Query().Collections.AnyAsync(c => c.ID == created.ID && c.UserID == user.ID)).ShouldBeTrue();
    }

    [Fact]
    public async Task Create_NamesAreUniquePerUserOnly()
    {
        var user = await Data.UserAsync();
        var other = await Data.UserAsync();
        await Collections.CreateCollectionAsync(user.ID, new CreateCollectionDto { Name = "Classics" });

        (await Collections.CreateCollectionAsync(user.ID, new CreateCollectionDto { Name = "classics" })).ShouldBeNull();
        (await Collections.CreateCollectionAsync(other.ID, new CreateCollectionDto { Name = "Classics" })).ShouldNotBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_BlankName_IsRejected(string name)
    {
        var user = await Data.UserAsync();

        (await Collections.CreateCollectionAsync(user.ID, new CreateCollectionDto { Name = name })).ShouldBeNull();
    }

    [Fact]
    public async Task Update_RenamesAndKeepsTheMediaCount()
    {
        var user = await Data.UserAsync();
        var collection = await Data.CollectionAsync(user, media: [await Data.MediaAsync(), await Data.MediaAsync()]);

        var (success, _, _, updated) = await Collections.UpdateCollectionAsync(user.ID,
            new UpdateCollectionDto { CollectionID = collection.ID, Name = "Renamed", VisibilityLevel = VisibilityLevel.Private });

        success.ShouldBeTrue();
        updated!.Name.ShouldBe("Renamed");
        updated.VisibilityLevel.ShouldBe(VisibilityLevel.Private);
        updated.MediaCount.ShouldBe(2);
    }

    [Fact]
    public async Task Update_CaseOnlyRename_IsAllowed()
    {
        var user = await Data.UserAsync();
        var collection = await Data.CollectionAsync(user, name: "sci-fi");

        var (success, _, _, updated) = await Collections.UpdateCollectionAsync(user.ID,
            new UpdateCollectionDto { CollectionID = collection.ID, Name = "Sci-Fi", VisibilityLevel = VisibilityLevel.Public });

        success.ShouldBeTrue();
        updated!.Name.ShouldBe("Sci-Fi");
    }

    [Fact]
    public async Task Update_NameOfAnotherCollection_IsRejected()
    {
        var user = await Data.UserAsync();
        await Data.CollectionAsync(user, name: "Horror");
        var collection = await Data.CollectionAsync(user, name: "Comedy");

        var (success, _, message, _) = await Collections.UpdateCollectionAsync(user.ID,
            new UpdateCollectionDto { CollectionID = collection.ID, Name = "horror" });

        success.ShouldBeFalse();
        message.ShouldBe("You already have a collection with this name.");
    }

    [Fact]
    public async Task UpdateAndDelete_OtherUsersCollection_IsNotFound()
    {
        var owner = await Data.UserAsync();
        var other = await Data.UserAsync();
        var collection = await Data.CollectionAsync(owner);

        (await Collections.UpdateCollectionAsync(other.ID, new UpdateCollectionDto { CollectionID = collection.ID, Name = "Mine" })).NotFound.ShouldBeTrue();
        (await Collections.DeleteCollectionAsync(other.ID, collection.ID)).NotFound.ShouldBeTrue();
        (await Query().Collections.AnyAsync(c => c.ID == collection.ID)).ShouldBeTrue();
    }

    [Fact]
    public async Task Delete_RemovesTheCollectionButKeepsTheMedia()
    {
        var user = await Data.UserAsync();
        var media = await Data.MediaAsync();
        var collection = await Data.CollectionAsync(user, media: [media]);

        (await Collections.DeleteCollectionAsync(user.ID, collection.ID)).Success.ShouldBeTrue();

        (await Query().Collections.AnyAsync(c => c.ID == collection.ID)).ShouldBeFalse();
        (await Query().CollectionMedias.AnyAsync(cm => cm.CollectionID == collection.ID)).ShouldBeFalse();
        (await Query().Media.AnyAsync(m => m.ID == media.ID)).ShouldBeTrue();
    }

    [Fact]
    public async Task DeletingMedia_RemovesItFromCollections()
    {
        var user = await Data.UserAsync();
        var media = await Data.MediaAsync();
        var collection = await Data.CollectionAsync(user, media: [media]);

        await Query().Media.Where(m => m.ID == media.ID).ExecuteDeleteAsync();

        (await Query().CollectionMedias.AnyAsync(cm => cm.CollectionID == collection.ID)).ShouldBeFalse();
    }

    /* Media in collections */

    [Fact]
    public async Task AddMedia_AppendsAtTheEndAndRejectsDuplicates()
    {
        var user = await Data.UserAsync();
        var existing = await Data.MediaAsync();
        var collection = await Data.CollectionAsync(user, media: [existing]);
        var externalId = TestData.Unique("7");

        (await Collections.AddMediaToCollectionAsync(user.ID, collection.ID, MediaType.Movie, externalId)).ShouldBeTrue();
        (await Collections.AddMediaToCollectionAsync(user.ID, collection.ID, MediaType.Movie, externalId)).ShouldBeFalse();

        var items = await Query().CollectionMedias.Include(cm => cm.Media).Where(cm => cm.CollectionID == collection.ID).OrderBy(cm => cm.OrderIndex).ToListAsync();
        items.Select(i => i.OrderIndex).ShouldBe([0, 1]);
        items[1].Media.ExternalApiID.ShouldBe(externalId);
    }

    [Fact]
    public async Task AddMedia_ToSomeoneElsesCollection_IsRejected()
    {
        var owner = await Data.UserAsync();
        var collection = await Data.CollectionAsync(owner);

        (await Collections.AddMediaToCollectionAsync((await Data.UserAsync()).ID, collection.ID, MediaType.Movie, TestData.Unique("7"))).ShouldBeFalse();
    }

    [Fact]
    public async Task RemoveMedia_ClosesTheGapInTheOrder()
    {
        var user = await Data.UserAsync();
        var (a, b, c) = (await Data.MediaAsync(), await Data.MediaAsync(), await Data.MediaAsync());
        var collection = await Data.CollectionAsync(user, media: [a, b, c]);

        (await Collections.RemoveMediaFromCollectionAsync(user.ID, collection.ID, b.MediaType, b.ExternalApiID)).ShouldBeTrue();

        var items = await Query().CollectionMedias.Where(cm => cm.CollectionID == collection.ID).OrderBy(cm => cm.OrderIndex).ToListAsync();
        items.Select(i => (i.MediaID, i.OrderIndex)).ShouldBe([(a.ID, 0), (c.ID, 1)]);
        (await Collections.RemoveMediaFromCollectionAsync(user.ID, collection.ID, b.MediaType, b.ExternalApiID)).ShouldBeFalse();
    }

    [Fact]
    public async Task RemoveMedia_FromSomeoneElsesCollection_IsRejected()
    {
        var owner = await Data.UserAsync();
        var media = await Data.MediaAsync();
        var collection = await Data.CollectionAsync(owner, media: [media]);

        (await Collections.RemoveMediaFromCollectionAsync((await Data.UserAsync()).ID, collection.ID, media.MediaType, media.ExternalApiID)).ShouldBeFalse();
        (await OrderAsync(collection)).ShouldBe([media.ID]);
    }

    [Theory]
    [InlineData(0, 3, new[] { 1, 2, 3, 0 })] // dragged down to the end
    [InlineData(3, 0, new[] { 3, 0, 1, 2 })] // dragged up to the start
    [InlineData(1, 2, new[] { 0, 2, 1, 3 })]
    [InlineData(2, 99, new[] { 0, 1, 3, 2 })] // past the end is clamped
    [InlineData(2, -5, new[] { 2, 0, 1, 3 })] // before the start is clamped
    [InlineData(1, 1, new[] { 0, 1, 2, 3 })] // no move
    public async Task Reorder_MovesOneItemAndShiftsTheOthers(int from, int to, int[] expected)
    {
        var user = await Data.UserAsync();
        var media = new[] { await Data.MediaAsync(), await Data.MediaAsync(), await Data.MediaAsync(), await Data.MediaAsync() };
        var collection = await Data.CollectionAsync(user, media: media);

        (await Collections.ReorderMediaAsync(user.ID, collection.ID, media[from].ID, to)).ShouldBeTrue();

        (await OrderAsync(collection)).ShouldBe(expected.Select(i => media[i].ID));
        var indexes = await Query().CollectionMedias.Where(cm => cm.CollectionID == collection.ID).Select(cm => cm.OrderIndex).OrderBy(i => i).ToListAsync();
        indexes.ShouldBe([0, 1, 2, 3]);
    }

    [Fact]
    public async Task Reorder_ForeignCollectionOrMedia_IsRejected()
    {
        var owner = await Data.UserAsync();
        var media = await Data.MediaAsync();
        var collection = await Data.CollectionAsync(owner, media: [media]);

        (await Collections.ReorderMediaAsync((await Data.UserAsync()).ID, collection.ID, media.ID, 0)).ShouldBeFalse();
        (await Collections.ReorderMediaAsync(owner.ID, collection.ID, (await Data.MediaAsync()).ID, 0)).ShouldBeFalse();
    }

    /* Visibility and listing */

    [Fact]
    public async Task List_VisibilityDependsOnTheViewer()
    {
        var owner = await Data.UserAsync();
        var follower = await Data.UserAsync();
        await Data.FollowAsync(follower, owner);
        var @public = await Data.CollectionAsync(owner, VisibilityLevel.Public);
        var followersOnly = await Data.CollectionAsync(owner, VisibilityLevel.FollowersOnly);
        var @private = await Data.CollectionAsync(owner, VisibilityLevel.Private);
        var favourites = await Data.FavouritesAsync(owner);

        (await Collections.GetUserCollectionsAsync(owner.Username, owner.ID))!.Select(c => c.ID)
            .ShouldBe([favourites.ID, @public.ID, followersOnly.ID, @private.ID], ignoreOrder: true);
        (await Collections.GetUserCollectionsAsync(owner.Username, follower.ID))!.Select(c => c.ID)
            .ShouldBe([@public.ID, followersOnly.ID], ignoreOrder: true);
        (await Collections.GetUserCollectionsAsync(owner.Username, null))!.Select(c => c.ID).ShouldBe([@public.ID]);
    }

    [Fact]
    public async Task List_UnknownUser_IsNull()
    {
        (await Collections.GetUserCollectionsAsync(TestData.Unique("nobody"), null)).ShouldBeNull();
    }

    [Theory]
    [InlineData("name_asc", new[] { "Alpha", "Beta", "Gamma" })]
    [InlineData("name_desc", new[] { "Gamma", "Beta", "Alpha" })]
    public async Task List_SortsByName(string sortBy, string[] expected)
    {
        var owner = await Data.UserAsync();
        foreach (var name in new[] { "Beta", "Gamma", "Alpha" })
            await Data.CollectionAsync(owner, name: name);

        (await Collections.GetUserCollectionsAsync(owner.Username, null, sortBy))!.Select(c => c.Name).ShouldBe(expected);
    }

    [Fact]
    public async Task Open_ShowsMediaInOrderWithVisibilityRules()
    {
        var owner = await Data.UserAsync();
        var (first, second) = (await Data.MediaAsync(title: "First"), await Data.MediaAsync(title: "Second"));
        var collection = await Data.CollectionAsync(owner, VisibilityLevel.FollowersOnly, media: [first, second]);
        var follower = await Data.UserAsync();
        await Data.FollowAsync(follower, owner);

        var asFollower = await Collections.OpenCollectionAsync(follower.ID, collection.ID);

        asFollower!.Collection.MediaCount.ShouldBe(2);
        asFollower.Collection.IsOwner.ShouldBeFalse();
        asFollower.MediaItems.Select(m => m.Media.Title).ShouldBe(["First", "Second"]);
        (await Collections.OpenCollectionAsync(null, collection.ID)).ShouldBeNull();
        (await Collections.OpenCollectionAsync(owner.ID, collection.ID))!.Collection.IsOwner.ShouldBeTrue();
        (await Collections.OpenCollectionAsync(owner.ID, -1)).ShouldBeNull();
    }

    [Fact]
    public async Task Memberships_ListEveryMediaOfTheUsersOwnCollections()
    {
        var user = await Data.UserAsync();
        var (movie, song) = (await Data.MediaAsync(MediaType.Movie), await Data.MediaAsync(MediaType.Music));
        var favourites = await Data.FavouritesAsync(user);
        await Data.AddToCollectionAsync(favourites, movie);
        var list = await Data.CollectionAsync(user, media: [movie, song]);
        await Data.CollectionAsync(await Data.UserAsync(), media: [song]);

        var memberships = await Collections.GetMembershipsAsync(user.ID);

        memberships.Select(m => (m.CollectionID, m.MediaType, m.ExternalApiID)).ShouldBe(
        [
            (favourites.ID, MediaType.Movie, movie.ExternalApiID),
            (list.ID, MediaType.Movie, movie.ExternalApiID),
            (list.ID, MediaType.Music, song.ExternalApiID)
        ], ignoreOrder: true);
    }
}
