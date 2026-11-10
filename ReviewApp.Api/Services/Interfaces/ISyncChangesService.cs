using ReviewApp.Api.DTOs;

namespace ReviewApp.Api.Services.Interfaces;

/// <summary>
/// The pull half of the Android offline sync (see <c>Services/Sync/SyncChangesService.cs</c> for the algorithm).
/// </summary>
public interface ISyncChangesService
{
    /// <summary>Returns the user's own reviews and collections that changed since a cursor, plus tombstones of deleted ones.</summary>
    /// <param name="userId">The user whose content is read.</param>
    /// <param name="since">The cursor from the previous page; 0 means everything.</param>
    /// <param name="syncedAt">When the device last finished a sync; too long ago forces a full resync.</param>
    /// <param name="continued">True for a follow-up page of the same run.</param>
    /// <param name="limit">Page size.</param>
    /// <param name="cancellationToken">Cancelled when the client disconnects.</param>
    Task<SyncChangesDto> GetChangesAsync(int userId, long since, DateTime? syncedAt, bool continued, int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Filters other users' reviews and collections cached on the device down to those the user can still see.</summary>
    /// <param name="userId">The user whose visibility is applied.</param>
    /// <param name="reviewIds">Cached review IDs.</param>
    /// <param name="collectionIds">Cached collection IDs.</param>
    /// <param name="cancellationToken">Cancelled when the client disconnects.</param>
    Task<SyncVisibleDto> GetVisibleAsync(int userId, IReadOnlyCollection<int> reviewIds, IReadOnlyCollection<int> collectionIds,
        CancellationToken cancellationToken = default);
}
