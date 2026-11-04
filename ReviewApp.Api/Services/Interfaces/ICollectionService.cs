using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services;

namespace ReviewApp.Api.Services.Interfaces;

public interface ICollectionService
{
    Task<CollectionDto?> CreateCollectionAsync(int userId, CreateCollectionDto dto);
    Task<(bool Success, bool NotFound, string Message, CollectionDto? Collection)> UpdateCollectionAsync(int userId, UpdateCollectionDto dto);
    Task<(bool Success, bool NotFound, string Message)> DeleteCollectionAsync(int userId, int collectionId);

    Task<bool> AddMediaToCollectionAsync(int userId, int collectionId, MediaType type, string externalApiId);
    Task<bool> RemoveMediaFromCollectionAsync(int userId, int collectionId, MediaType type, string externalApiId);
    Task<bool> ReorderMediaAsync(int userId, int collectionId, int dbMediaId, int newOrderIndex);

    // Core writes behind the methods above, also used by the offline sync: exact statuses, stored media IDs,
    // no achievement evaluation (the caller evaluates after committing)
    Task<CollectionWriteResult> CreateCoreAsync(int userId, string name, VisibilityLevel visibility);
    Task<CollectionWriteResult> UpdateCoreAsync(int userId, int collectionId, string name, VisibilityLevel visibility);
    Task<CollectionWriteResult> DeleteCoreAsync(int userId, int collectionId);
    Task<CollectionWriteResult> AddMediaCoreAsync(int userId, int collectionId, int mediaId);
    Task<CollectionWriteResult> RemoveMediaCoreAsync(int userId, int collectionId, int mediaId);
    // The listed media first (in that order), the other items after them in their current order
    Task<CollectionWriteResult> SetOrderAsync(int userId, int collectionId, IReadOnlyList<int> mediaIdsInOrder);

    Task<List<CollectionDto>?> GetUserCollectionsAsync(string targetUsername, int? requestingUserId, string sortBy = "createdAt_asc");
    Task<CollectionWithMediasDto?> OpenCollectionAsync(int? requestingUserId, int collectionId);
    Task<CollectionWithMediasDto> SearchCollectionMediaAsync(int? requestingUserId, int collectionId, string query);
    Task<List<CollectionMembershipDto>> GetMembershipsAsync(int userId);
}
