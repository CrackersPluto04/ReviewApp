using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services;

namespace ReviewApp.Api.Services.Interfaces;

/// <summary>
/// Collections of media. Names are unique per user, and the default Favourites collection is permanent: it cannot
/// be renamed, edited or deleted. Visibility is decided per viewer, and a collection the viewer may not see is
/// reported as missing.
/// </summary>
/// <remarks>
/// The first group of methods serves the web API and returns messages. The <c>*Core</c> group beneath them does the
/// same writes with exact statuses and stored media IDs and is shared with the offline sync.
/// </remarks>
public interface ICollectionService
{
    /// <summary>Creates a collection for the user.</summary>
    /// <returns>The new collection, or null when the user already has one with this name.</returns>
    Task<CollectionDto?> CreateCollectionAsync(int userId, CreateCollectionDto dto);

    /// <summary>Renames a collection or changes its visibility.</summary>
    /// <returns>Success, whether the collection was not found, a message for the client, and the updated collection.</returns>
    Task<(bool Success, bool NotFound, string Message, CollectionDto? Collection)> UpdateCollectionAsync(int userId, UpdateCollectionDto dto);

    /// <summary>Deletes a collection with its item links; the media stay.</summary>
    /// <returns>Success, whether the collection was not found, and a message for the client.</returns>
    Task<(bool Success, bool NotFound, string Message)> DeleteCollectionAsync(int userId, int collectionId);

    /// <summary>Adds a title, fetching it from its source first if it is not stored yet.</summary>
    /// <returns>False when the title was not added, e.g. it is already in the collection.</returns>
    Task<bool> AddMediaToCollectionAsync(int userId, int collectionId, MediaType type, string externalApiId);

    /// <summary>Removes a title from a collection.</summary>
    /// <returns>False when the title was not in the collection.</returns>
    Task<bool> RemoveMediaFromCollectionAsync(int userId, int collectionId, MediaType type, string externalApiId);

    /// <summary>Moves one item to a new position in the collection's order.</summary>
    /// <param name="userId">The collection's owner.</param>
    /// <param name="collectionId">The collection to reorder.</param>
    /// <param name="dbMediaId">The stored media ID, not the external one.</param>
    /// <param name="newOrderIndex">The item's final position.</param>
    Task<bool> ReorderMediaAsync(int userId, int collectionId, int dbMediaId, int newOrderIndex);

    // Core writes behind the methods above, also used by the offline sync: exact statuses, stored media IDs,
    // no achievement evaluation (the caller evaluates after committing).

    /// <summary>Core of <see cref="CreateCollectionAsync"/>.</summary>
    Task<CollectionWriteResult> CreateCoreAsync(int userId, string name, VisibilityLevel visibility);

    /// <summary>Core of <see cref="UpdateCollectionAsync"/>.</summary>
    Task<CollectionWriteResult> UpdateCoreAsync(int userId, int collectionId, string name, VisibilityLevel visibility);

    /// <summary>Core of <see cref="DeleteCollectionAsync"/>.</summary>
    Task<CollectionWriteResult> DeleteCoreAsync(int userId, int collectionId);

    /// <summary>Core of <see cref="AddMediaToCollectionAsync"/>; adding a title that is already there is reported as <c>AlreadyPresent</c> rather than as a failure.</summary>
    /// <param name="userId">The collection's owner.</param>
    /// <param name="collectionId">The collection to add to.</param>
    /// <param name="mediaId">The stored media ID.</param>
    Task<CollectionWriteResult> AddMediaCoreAsync(int userId, int collectionId, int mediaId);

    /// <summary>Core of <see cref="RemoveMediaFromCollectionAsync"/>.</summary>
    /// <param name="userId">The collection's owner.</param>
    /// <param name="collectionId">The collection to remove from.</param>
    /// <param name="mediaId">The stored media ID.</param>
    Task<CollectionWriteResult> RemoveMediaCoreAsync(int userId, int collectionId, int mediaId);

    /// <summary>
    /// Sets the whole order at once: the listed media come first, in that order, and the other items follow in
    /// their current order.
    /// </summary>
    /// <param name="userId">The collection's owner.</param>
    /// <param name="collectionId">The collection to reorder.</param>
    /// <param name="mediaIdsInOrder">Stored media IDs.</param>
    Task<CollectionWriteResult> SetOrderAsync(int userId, int collectionId, IReadOnlyList<int> mediaIdsInOrder);

    /// <summary>Lists the collections of a user that the viewer may see.</summary>
    /// <param name="targetUsername">The profile owner.</param>
    /// <param name="requestingUserId">The viewer, or null for an anonymous visitor.</param>
    /// <param name="sortBy">A sort key such as <c>createdAt_asc</c>.</param>
    /// <returns>The collections, or null if the user does not exist.</returns>
    Task<List<CollectionDto>?> GetUserCollectionsAsync(string targetUsername, int? requestingUserId, string sortBy = "createdAt_asc");

    /// <summary>Opens a collection with its media.</summary>
    /// <returns>The collection, or null when it does not exist or is hidden from the viewer.</returns>
    Task<CollectionWithMediasDto?> OpenCollectionAsync(int? requestingUserId, int collectionId);

    /// <summary>
    /// Reserved for searching within a collection's media. Not implemented yet (it throws
    /// <see cref="NotImplementedException"/>) and not called from any endpoint.
    /// </summary>
    Task<CollectionWithMediasDto> SearchCollectionMediaAsync(int? requestingUserId, int collectionId, string query);

    /// <summary>
    /// Lists every title in each of the user's collections, so clients can show favourite and collection state
    /// without one request per media card.
    /// </summary>
    Task<List<CollectionMembershipDto>> GetMembershipsAsync(int userId);
}
