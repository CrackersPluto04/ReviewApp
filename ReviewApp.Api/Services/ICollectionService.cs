using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;

namespace ReviewApp.Api.Services;

public interface ICollectionService
{
    Task<CollectionDto?> CreateCollectionAsync(int userId, CreateCollectionDto dto);
    Task<CollectionDto?> UpdateCollectionAsync(int userId, UpdateCollectionDto dto);
    Task<bool> DeleteCollectionAsync(int userId, int collectionId);

    Task<bool> AddMediaToCollectionAsync(int userId, int collectionId, MediaType type, string externalApiId);
    Task<bool> RemoveMediaFromCollectionAsync(int userId, int collectionId, int mediaId);
    Task<bool> ReorderMediaAsync(int userId, int collectionId, int mediaId, int newOrderIndex);

    Task<List<CollectionDto>> GetUserCollectionsAsync(string targetUsername, int? requestingUserId, string sortBy = "createdAt_desc");
    Task<CollectionWithMediasDto?> OpenCollectionAsync(int? requestingUserId, int collectionId);
    Task<CollectionWithMediasDto> SearchCollectionMediaAsync(int? requestingUserId, int collectionId, string query);
}
