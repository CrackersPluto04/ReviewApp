using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Services;

/// <summary>
/// Collections of media. Names are unique per user (case-insensitive, enforced by a unique index as well as a
/// check), and the default Favourites collection, created by a database trigger, is permanent: its name always
/// identifies it because no other collection can take it. Every change to a collection or its items sets
/// <c>UpdatedAt</c>, which moves the row's <c>rowversion</c> and so reaches offline devices through the changes feed.
/// </summary>
/// <remarks>
/// The public methods keep the tuples and messages the web app uses; the <c>*Core</c> methods report exact
/// statuses, take stored media IDs and never evaluate achievements, because the offline sync shares them.
/// </remarks>
public class CollectionService : ICollectionService
{
    private const string DefaultCollectionLockedMessage = "Your Favourites collection can't be renamed, edited or deleted.";

    private readonly AppDbContext _context;
    private readonly IMediaService _mediaService;
    private readonly IAchievementService _achievementService;

    public CollectionService(AppDbContext context, IMediaService mediaService, IAchievementService achievementService)
    {
        _context = context;
        _mediaService = mediaService;
        _achievementService = achievementService;
    }

    public const int MaxNameLength = 50;

    public async Task<CollectionDto?> CreateCollectionAsync(int userId, CreateCollectionDto dto)
    {
        var result = await CreateCoreAsync(userId, dto.Name, dto.VisibilityLevel);
        if (result.Status != CollectionWriteStatus.Created)
            return null;

        await _achievementService.EvaluateAsync(userId, CreateMetrics);

        var collection = result.Collection!;
        return new CollectionDto
        {
            ID = collection.ID,
            Name = collection.Name,
            VisibilityLevel = collection.VisibilityLevel,
            CreatedAt = collection.CreatedAt.ToString("yyyy-MM-dd"),
            MediaCount = 0,
            IsOwner = true
        };
    }

    public async Task<(bool Success, bool NotFound, string Message, CollectionDto? Collection)> UpdateCollectionAsync(int userId, UpdateCollectionDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return (false, false, "Collection name is required.", null);

        var result = await UpdateCoreAsync(userId, dto.CollectionID, dto.Name, dto.VisibilityLevel);
        switch (result.Status)
        {
            case CollectionWriteStatus.NotFound: return (false, true, "Collection not found.", null);
            case CollectionWriteStatus.Locked: return (false, false, DefaultCollectionLockedMessage, null);
            case CollectionWriteStatus.NameTaken: return (false, false, "You already have a collection with this name.", null);
            case CollectionWriteStatus.InvalidName: return (false, false, "Collection name is required.", null);
        }

        var collection = result.Collection!;
        // The media items aren't loaded, so they are counted in the database
        var mediaCount = await _context.CollectionMedias.CountAsync(cm => cm.CollectionID == collection.ID);

        return (true, false, "Collection updated successfully!", new CollectionDto
        {
            ID = collection.ID,
            Name = collection.Name,
            VisibilityLevel = collection.VisibilityLevel,
            CreatedAt = collection.CreatedAt.ToString("yyyy-MM-dd"),
            MediaCount = mediaCount,
            IsOwner = true
        });
    }

    public async Task<(bool Success, bool NotFound, string Message)> DeleteCollectionAsync(int userId, int collectionId)
    {
        var result = await DeleteCoreAsync(userId, collectionId);
        return result.Status switch
        {
            CollectionWriteStatus.Deleted => (true, false, "Collection deleted successfully!"),
            CollectionWriteStatus.Locked => (false, false, DefaultCollectionLockedMessage),
            _ => (false, true, "Collection not found.")
        };
    }

    public async Task<bool> AddMediaToCollectionAsync(int userId, int collectionId, MediaType type, string externalApiId)
    {
        // Ownership first, so someone else's collection is never a reason to fetch media
        if (!await _context.Collections.AnyAsync(c => c.ID == collectionId && c.UserID == userId))
            return false;

        var mediaId = await _mediaService.GetOrCreateMediaAsync(type, externalApiId);
        if (mediaId == -1) return false;

        return (await AddMediaCoreAsync(userId, collectionId, mediaId)).Status == CollectionWriteStatus.Added;
    }

    public async Task<bool> RemoveMediaFromCollectionAsync(int userId, int collectionId, MediaType type, string externalApiId)
    {
        var mediaId = await _context.Media
            .Where(m => m.MediaType == type && m.ExternalApiID == externalApiId)
            .Select(m => (int?)m.ID)
            .FirstOrDefaultAsync();
        if (mediaId == null) return false;

        return (await RemoveMediaCoreAsync(userId, collectionId, mediaId.Value)).Status == CollectionWriteStatus.Removed;
    }

    public async Task<bool> ReorderMediaAsync(int userId, int collectionId, int dbMediaId, int newOrderIndex)
    {
        var collection = await _context.Collections
            .Include(c => c.CollectionMedias)
            .FirstOrDefaultAsync(c => c.ID == collectionId && c.UserID == userId);

        if (collection == null) return false;

        var mediaItem = collection.CollectionMedias.FirstOrDefault(cm => cm.MediaID == dbMediaId);
        if (mediaItem == null) return false;

        // The target index is clamped, so a client with a stale list can't leave gaps in the order
        var maxIndex = collection.CollectionMedias.Count - 1;
        var oldIndex = mediaItem.OrderIndex;
        if (newOrderIndex < 0) newOrderIndex = 0;
        if (newOrderIndex > maxIndex) newOrderIndex = maxIndex;

        if (newOrderIndex == oldIndex) return true; // No change needed, did not move

        // The items between the old and the new position shift by one to make room
        foreach (var item in collection.CollectionMedias)
        {
            if (item.MediaID == dbMediaId) continue;

            if (oldIndex < newOrderIndex) // Dragged down the list
            {
                if (item.OrderIndex > oldIndex && item.OrderIndex <= newOrderIndex)
                    item.OrderIndex--;
            }
            else // Dragged up the list
            {
                if (item.OrderIndex >= newOrderIndex && item.OrderIndex < oldIndex)
                    item.OrderIndex++;
            }
        }

        mediaItem.OrderIndex = newOrderIndex;
        collection.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return true;
    }

    /* Core writes (also used by the offline sync): exact statuses, stored media IDs, no achievement evaluation */

    // What creating a collection can change in the achievements
    public static readonly AchievementMetric[] CreateMetrics = [AchievementMetric.CollectionsCreated];

    public static bool IsValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Length <= MaxNameLength;

    public async Task<CollectionWriteResult> CreateCoreAsync(int userId, string name, VisibilityLevel visibility)
    {
        if (!IsValidName(name))
            return new CollectionWriteResult(CollectionWriteStatus.InvalidName);

        // Names are unique per user (case-insensitive collation), Favourites included
        var takenBy = await CollectionNamedAsync(userId, name, exceptId: null);
        if (takenBy != null)
            return new CollectionWriteResult(CollectionWriteStatus.NameTaken, ExistingCollectionID: takenBy);

        var collection = new Collection
        {
            UserID = userId,
            Name = name,
            VisibilityLevel = visibility
        };

        _context.Collections.Add(collection);
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            // A parallel request took the name first (unique index on user + name)
            _context.Entry(collection).State = EntityState.Detached;
            return new CollectionWriteResult(CollectionWriteStatus.NameTaken, ExistingCollectionID: await CollectionNamedAsync(userId, name, exceptId: null));
        }

        return new CollectionWriteResult(CollectionWriteStatus.Created, collection);
    }

    public async Task<CollectionWriteResult> UpdateCoreAsync(int userId, int collectionId, string name, VisibilityLevel visibility)
    {
        var collection = await _context.Collections
            .FirstOrDefaultAsync(c => c.ID == collectionId && c.UserID == userId);

        if (collection == null) return new CollectionWriteResult(CollectionWriteStatus.NotFound);
        if (collection.Name == Collection.DefaultName) return new CollectionWriteResult(CollectionWriteStatus.Locked, collection);
        if (!IsValidName(name)) return new CollectionWriteResult(CollectionWriteStatus.InvalidName, collection);

        if (collection.Name != name)
        {
            // Excludes this collection, so a case-only rename isn't blocked by its own (case-insensitive) name
            var takenBy = await CollectionNamedAsync(userId, name, exceptId: collection.ID);
            if (takenBy != null)
                return new CollectionWriteResult(CollectionWriteStatus.NameTaken, collection, takenBy);
        }

        collection.Name = name;
        collection.VisibilityLevel = visibility;
        collection.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            // A parallel request took the name first
            await _context.Entry(collection).ReloadAsync();
            return new CollectionWriteResult(CollectionWriteStatus.NameTaken, collection, await CollectionNamedAsync(userId, name, exceptId: collection.ID));
        }

        return new CollectionWriteResult(CollectionWriteStatus.Updated, collection);
    }

    public async Task<CollectionWriteResult> DeleteCoreAsync(int userId, int collectionId)
    {
        var collection = await _context.Collections
            .FirstOrDefaultAsync(c => c.ID == collectionId && c.UserID == userId);

        if (collection == null) return new CollectionWriteResult(CollectionWriteStatus.NotFound);
        if (collection.Name == Collection.DefaultName) return new CollectionWriteResult(CollectionWriteStatus.Locked, collection);

        _context.Collections.Remove(collection);
        // Tells offline devices to drop their copy (same SaveChanges, so both or neither happen)
        _context.SyncTombstones.Add(new SyncTombstone { UserID = userId, EntityType = SyncEntityType.Collection, EntityID = collection.ID });
        await _context.SaveChangesAsync();

        return new CollectionWriteResult(CollectionWriteStatus.Deleted, collection);
    }

    public async Task<CollectionWriteResult> AddMediaCoreAsync(int userId, int collectionId, int mediaId)
    {
        var collection = await _context.Collections
            .FirstOrDefaultAsync(c => c.ID == collectionId && c.UserID == userId);
        if (collection == null) return new CollectionWriteResult(CollectionWriteStatus.NotFound);

        if (await _context.CollectionMedias.AnyAsync(cm => cm.CollectionID == collectionId && cm.MediaID == mediaId))
            return new CollectionWriteResult(CollectionWriteStatus.AlreadyPresent, collection);

        // Put new media at the end of the collection
        var maxOrder = await _context.CollectionMedias
            .Where(cm => cm.CollectionID == collectionId)
            .MaxAsync(cm => (int?)cm.OrderIndex) ?? -1;

        var collectionMedia = new CollectionMedia
        {
            CollectionID = collectionId,
            MediaID = mediaId,
            OrderIndex = maxOrder + 1
        };

        _context.CollectionMedias.Add(collectionMedia);
        collection.UpdatedAt = DateTime.UtcNow;
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            // A parallel request added it first (primary key): already in the collection
            _context.Entry(collectionMedia).State = EntityState.Detached;
            await _context.Entry(collection).ReloadAsync();
            return new CollectionWriteResult(CollectionWriteStatus.AlreadyPresent, collection);
        }

        return new CollectionWriteResult(CollectionWriteStatus.Added, collection);
    }

    public async Task<CollectionWriteResult> RemoveMediaCoreAsync(int userId, int collectionId, int mediaId)
    {
        var collection = await _context.Collections
            .FirstOrDefaultAsync(c => c.ID == collectionId && c.UserID == userId);
        if (collection == null) return new CollectionWriteResult(CollectionWriteStatus.NotFound);

        var item = await _context.CollectionMedias
            .FirstOrDefaultAsync(cm => cm.CollectionID == collectionId && cm.MediaID == mediaId);
        if (item == null) return new CollectionWriteResult(CollectionWriteStatus.NotPresent, collection);

        _context.CollectionMedias.Remove(item);

        // Update order indexes of remaining media in collection
        var subsequentItems = await _context.CollectionMedias
            .Where(cm => cm.CollectionID == collectionId && cm.OrderIndex > item.OrderIndex)
            .ToListAsync();

        foreach (var subItem in subsequentItems)
            subItem.OrderIndex--;

        collection.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return new CollectionWriteResult(CollectionWriteStatus.Removed, collection);
    }

    /// <summary>
    /// Puts the listed media first, in the given order, and every other item of the collection after them in their
    /// current order. Media that are not in the collection (any more) are ignored, so an order made on another
    /// device never drops or invents items.
    /// </summary>
    public async Task<CollectionWriteResult> SetOrderAsync(int userId, int collectionId, IReadOnlyList<int> mediaIdsInOrder)
    {
        var collection = await _context.Collections
            .Include(c => c.CollectionMedias)
            .FirstOrDefaultAsync(c => c.ID == collectionId && c.UserID == userId);
        if (collection == null) return new CollectionWriteResult(CollectionWriteStatus.NotFound);

        var current = collection.CollectionMedias.OrderBy(cm => cm.OrderIndex).ToList();
        var ordered = MergeOrder(current.Select(cm => cm.MediaID).ToList(), mediaIdsInOrder);
        var byMedia = current.ToDictionary(cm => cm.MediaID);

        var changed = false;
        for (var index = 0; index < ordered.Count; index++)
        {
            var item = byMedia[ordered[index]];
            if (item.OrderIndex == index) continue;
            item.OrderIndex = index;
            changed = true;
        }

        if (changed)
        {
            collection.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        return new CollectionWriteResult(CollectionWriteStatus.Reordered, collection);
    }

    /// <summary>Merges a requested order with the current one: the requested items that are still present first, then the rest in their current order.</summary>
    /// <param name="currentOrder">The stored media IDs in their current order.</param>
    /// <param name="requestedOrder">The order asked for, possibly with IDs that are gone or repeated.</param>
    public static List<int> MergeOrder(IReadOnlyList<int> currentOrder, IReadOnlyList<int> requestedOrder)
    {
        var present = currentOrder.ToHashSet();
        var result = requestedOrder.Where(present.Contains).Distinct().ToList();
        var placed = result.ToHashSet();
        result.AddRange(currentOrder.Where(id => !placed.Contains(id)));
        return result;
    }

    private Task<int?> CollectionNamedAsync(int userId, string name, int? exceptId) =>
        _context.Collections
            .Where(c => c.UserID == userId && c.Name == name && c.ID != exceptId)
            .Select(c => (int?)c.ID)
            .FirstOrDefaultAsync();

    public async Task<List<CollectionDto>?> GetUserCollectionsAsync(string targetUsername, int? requestingUserId, string sortBy = "createdAt_asc")
    {
        var targetUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == targetUsername);
        if (targetUser == null) return null;

        bool isOwner = requestingUserId.HasValue && requestingUserId.Value == targetUser.ID;

        var isFollower = false;
        if (requestingUserId.HasValue && !isOwner)
            isFollower = await _context.UserFollowers.AnyAsync(uf =>
                uf.FollowerID == requestingUserId.Value &&
                uf.FollowingID == targetUser.ID);

        var query = _context.Collections.Where(c => c.UserID == targetUser.ID);

        if (!isOwner)
        {
            if (isFollower)
                query = query.Where(c =>
                    c.VisibilityLevel == VisibilityLevel.Public ||
                    c.VisibilityLevel == VisibilityLevel.FollowersOnly);
            else
                query = query.Where(c => c.VisibilityLevel == VisibilityLevel.Public);
        }

        query = sortBy switch
        {
            "createdAt_desc" => query.OrderByDescending(c => c.CreatedAt),
            "name_asc" => query.OrderBy(c => c.Name),
            "name_desc" => query.OrderByDescending(c => c.Name),
            _ => query.OrderBy(c => c.CreatedAt)
        };

        var selectedQuery = query.Select(c => new CollectionDto
        {
            ID = c.ID,
            Name = c.Name,
            VisibilityLevel = c.VisibilityLevel,
            CreatedAt = c.CreatedAt.ToString("yyyy-MM-dd"),
            MediaCount = c.CollectionMedias.Count,
            IsOwner = isOwner,
            IsDefault = c.Name == Collection.DefaultName
        });

        return await selectedQuery.ToListAsync();
    }

    public async Task<CollectionWithMediasDto?> OpenCollectionAsync(int? requestingUserId, int collectionId)
    {
        var query = _context.Collections.Where(c => c.ID == collectionId);

        // The owner is looked up first because the visibility filter below depends on who owns it
        var targetUserId = await query
            .Select(c => c.UserID)
            .FirstOrDefaultAsync();

        var isOwner = requestingUserId.HasValue && requestingUserId.Value == targetUserId;

        var isFollower = false;
        if (requestingUserId.HasValue && !isOwner)
            isFollower = await _context.UserFollowers.AnyAsync(uf =>
                uf.FollowerID == requestingUserId.Value &&
                uf.FollowingID == targetUserId);

        if (!isOwner)
        {
            if (isFollower)
                query = query.Where(c =>
                    c.VisibilityLevel == VisibilityLevel.Public ||
                    c.VisibilityLevel == VisibilityLevel.FollowersOnly);
            else
                query = query.Where(r => r.VisibilityLevel == VisibilityLevel.Public);
        }

        var rawCollection = await query
            .Select(c => new
            {
                c.ID,
                c.Name,
                c.VisibilityLevel,
                c.CreatedAt,
                MediaCount = c.CollectionMedias.Count,
                c.UserID,
                RawMedias = c.CollectionMedias
                    .OrderBy(cm => cm.OrderIndex)
                    .Select(cm => new
                    {
                        cm.MediaID,
                        cm.Media,
                        cm.OrderIndex,
                        cm.AddedAt
                    })
            })
            .FirstOrDefaultAsync();

        if (rawCollection == null)
            return null;

        var result = new CollectionWithMediasDto
        {
            Collection = new CollectionDto
            {
                ID = rawCollection.ID,
                Name = rawCollection.Name,
                VisibilityLevel = rawCollection.VisibilityLevel,
                CreatedAt = rawCollection.CreatedAt.ToString("yyyy-MM-dd"),
                MediaCount = rawCollection.MediaCount,
                IsOwner = requestingUserId.HasValue && requestingUserId.Value == rawCollection.UserID,
                IsDefault = rawCollection.Name == Collection.DefaultName
            },
            MediaItems = rawCollection.RawMedias.Select(rm => new CollectionMediaDto
            {
                DbMediaID = rm.MediaID,
                Media = _mediaService.ToMediaDto(rm.Media.MediaType, rm.Media),
                OrderIndex = rm.OrderIndex,
                AddedAt = rm.AddedAt
            }).ToList()
        };

        return result;
    }

    public async Task<CollectionWithMediasDto> SearchCollectionMediaAsync(int? requestingUserId, int collectionId, string query)
    {
        throw new NotImplementedException();
    }

    public async Task<List<CollectionMembershipDto>> GetMembershipsAsync(int userId)
    {
        // Every media in the user's own collections, so a client can tell what is already added
        return await _context.CollectionMedias
            .AsNoTracking()
            .Where(cm => cm.Collection.UserID == userId)
            .Select(cm => new CollectionMembershipDto
            {
                CollectionID = cm.CollectionID,
                MediaType = cm.Media.MediaType,
                ExternalApiID = cm.Media.ExternalApiID
            })
            .ToListAsync();
    }
}