using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;

namespace ReviewApp.Api.Services;

public class CollectionService : ICollectionService
{
    private readonly AppDbContext _context;
    private readonly IMediaService _mediaService;

    public CollectionService(AppDbContext context, IMediaService mediaService)
    {
        _context = context;
        _mediaService = mediaService;
    }

    public async Task<CollectionDto?> CreateCollectionAsync(int userId, CreateCollectionDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return null;

        var exists = await _context.Collections
            .AnyAsync(c => c.UserID == userId && c.Name == dto.Name);

        if (exists) return null;

        var collection = new Collection
        {
            UserID = userId,
            Name = dto.Name,
            VisibilityLevel = dto.VisibilityLevel
        };

        _context.Collections.Add(collection);
        await _context.SaveChangesAsync();

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

    public async Task<CollectionDto?> UpdateCollectionAsync(int userId, UpdateCollectionDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return null;

        var collection = await _context.Collections
            .FirstOrDefaultAsync(c => c.ID == dto.CollectionID && c.UserID == userId);

        if (collection == null) return null;

        if (collection.Name != dto.Name)
        {
            var nameTaken = await _context.Collections
                .AnyAsync(c => c.UserID == userId && c.Name == dto.Name);

            if (nameTaken) return null;
        }

        collection.Name = dto.Name;
        collection.VisibilityLevel = dto.VisibilityLevel;

        await _context.SaveChangesAsync();

        return new CollectionDto
        {
            ID = collection.ID,
            Name = collection.Name,
            VisibilityLevel = collection.VisibilityLevel,
            CreatedAt = collection.CreatedAt.ToString("yyyy-MM-dd"),
            MediaCount = collection.CollectionMedias.Count,
            IsOwner = true
        };
    }

    public async Task<bool> DeleteCollectionAsync(int userId, int collectionId)
    {
        var collection = await _context.Collections
            .FirstOrDefaultAsync(c => c.ID == collectionId && c.UserID == userId);

        if (collection == null) return false;

        _context.Collections.Remove(collection);
        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<bool> AddMediaToCollectionAsync(int userId, int collectionId, MediaType type, string externalApiId)
    {
        // Check if collection exists and belongs to user
        var collectionExists = await _context.Collections
            .AnyAsync(c => c.ID == collectionId && c.UserID == userId);

        if (!collectionExists) return false;

        // Check if media exists, if not create it
        var mediaId = await _mediaService.GetOrCreateMediaAsync(type, externalApiId);

        if (mediaId == -1) return false;

        // Check if media already in collection
        var alreadyInCollection = await _context.CollectionMedias
            .AnyAsync(cm => cm.CollectionID == collectionId && cm.MediaID == mediaId);

        if (alreadyInCollection) return false;

        // Get max order index in collection
        var maxOrder = await _context.CollectionMedias
            .Where(cm => cm.CollectionID == collectionId)
            .MaxAsync(cm => (int?)cm.OrderIndex) ?? -1;

        var collectionMedia = new CollectionMedia
        {
            CollectionID = collectionId,
            MediaID = mediaId,
            OrderIndex = maxOrder + 1 // Put new media at the end of the collection
        };

        _context.CollectionMedias.Add(collectionMedia);
        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<bool> RemoveMediaFromCollectionAsync(int userId, int collectionId, MediaType type, string externalApiId)
    {
        // Check if collection belongs to user and media is in collection
        var item = await _context.CollectionMedias
            .Include(cm => cm.Collection)
            .FirstOrDefaultAsync(cm => cm.CollectionID == collectionId && cm.Media.MediaType == type && cm.Media.ExternalApiID == externalApiId && cm.Collection.UserID == userId);

        if (item == null) return false;

        _context.CollectionMedias.Remove(item);

        // Update order indexes of remaining media in collection
        var subsequentItems = await _context.CollectionMedias
            .Where(cm => cm.CollectionID == collectionId && cm.OrderIndex > item.OrderIndex)
            .ToListAsync();

        foreach (var subItem in subsequentItems)
            subItem.OrderIndex--;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ReorderMediaAsync(int userId, int collectionId, int dbMediaId, int newOrderIndex)
    {
        // Get collection and its media items
        var collection = await _context.Collections
            .Include(c => c.CollectionMedias)
            .FirstOrDefaultAsync(c => c.ID == collectionId && c.UserID == userId);

        if (collection == null) return false;

        // Find the CollectionMedia item to reorder
        var mediaItem = collection.CollectionMedias.FirstOrDefault(cm => cm.MediaID == dbMediaId);
        if (mediaItem == null) return false;

        // Get current order index and desired new index
        var maxIndex = collection.CollectionMedias.Count - 1;
        var oldIndex = mediaItem.OrderIndex;
        if (newOrderIndex < 0) newOrderIndex = 0;
        if (newOrderIndex > maxIndex) newOrderIndex = maxIndex;

        if (newOrderIndex == oldIndex) return true; // No change needed, did not move

        // Move the media item to the new index
        foreach (var item in collection.CollectionMedias)
        {
            if (item.MediaID == dbMediaId) continue; // Skip the item we are moving

            if (oldIndex < newOrderIndex) // Dragged DOWN the list
            {
                if (item.OrderIndex > oldIndex && item.OrderIndex <= newOrderIndex)
                    item.OrderIndex--;
            }
            else // Dragged UP the list
            {
                if (item.OrderIndex >= newOrderIndex && item.OrderIndex < oldIndex)
                    item.OrderIndex++;
            }
        }

        mediaItem.OrderIndex = newOrderIndex;
        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<List<CollectionDto>> GetUserCollectionsAsync(string targetUsername, int? requestingUserId, string sortBy = "createdAt_asc")
    {
        // Find target user by username
        var targetUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == targetUsername);
        if (targetUser == null) return null!;

        // Check if requesting user is the owner of the collections
        // or a follower of the owner
        bool isOwner = requestingUserId.HasValue && requestingUserId.Value == targetUser.ID;

        var isFollower = false;
        if (requestingUserId.HasValue && !isOwner)
            isFollower = await _context.UserFollowers.AnyAsync(uf =>
                uf.FollowerID == requestingUserId.Value &&
                uf.FollowingID == targetUser.ID);

        // Build the query to get collections, applying visibility filter
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

        // Apply filters
        query = sortBy switch
        {
            "createdAt_desc" => query.OrderByDescending(c => c.CreatedAt),
            "name_asc" => query.OrderBy(c => c.Name),
            "name_desc" => query.OrderByDescending(c => c.Name),
            _ => query.OrderBy(c => c.CreatedAt)
        };

        // Map to DTO
        var selectedQuery = query.Select(c => new CollectionDto
        {
            ID = c.ID,
            Name = c.Name,
            VisibilityLevel = c.VisibilityLevel,
            CreatedAt = c.CreatedAt.ToString("yyyy-MM-dd"),
            MediaCount = c.CollectionMedias.Count,
            IsOwner = isOwner
        });

        return await selectedQuery.ToListAsync();
    }

    public async Task<CollectionWithMediasDto?> OpenCollectionAsync(int? requestingUserId, int collectionId)
    {
        // Build the query
        var query = _context.Collections.Where(c => c.ID == collectionId);

        // Get targerUserID
        var targetUserId = await query
            .Select(c => c.UserID)
            .FirstOrDefaultAsync();

        // Get visibility relationships
        var isOwner = requestingUserId.HasValue && requestingUserId.Value == targetUserId;

        var isFollower = false;
        if (requestingUserId.HasValue && !isOwner)
            isFollower = await _context.UserFollowers.AnyAsync(uf =>
                uf.FollowerID == requestingUserId.Value &&
                uf.FollowingID == targetUserId);

        // Apply visibility filter
        if (!isOwner)
        {
            if (isFollower)
                query = query.Where(c =>
                    c.VisibilityLevel == VisibilityLevel.Public ||
                    c.VisibilityLevel == VisibilityLevel.FollowersOnly);
            else
                query = query.Where(r => r.VisibilityLevel == VisibilityLevel.Public);
        }

        // Get collection with medias
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

        // Map to DTO
        var result = new CollectionWithMediasDto
        {
            Collection = new CollectionDto
            {
                ID = rawCollection.ID,
                Name = rawCollection.Name,
                VisibilityLevel = rawCollection.VisibilityLevel,
                CreatedAt = rawCollection.CreatedAt.ToString("yyyy-MM-dd"),
                MediaCount = rawCollection.MediaCount,
                IsOwner = requestingUserId.HasValue && requestingUserId.Value == rawCollection.UserID
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
}