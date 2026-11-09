using ReviewApp.Api.DTOs;

namespace ReviewApp.Api.Services.Interfaces;

public interface ISyncChangesService
{
    // The user's own content changed since the cursor (Services/Sync/SyncChangesService.cs)
    Task<SyncChangesDto> GetChangesAsync(int userId, long since, DateTime? syncedAt, bool continued, int limit,
        CancellationToken cancellationToken = default);

    // Which of these (other users') reviews and collections the user can still see
    Task<SyncVisibleDto> GetVisibleAsync(int userId, IReadOnlyCollection<int> reviewIds, IReadOnlyCollection<int> collectionIds,
        CancellationToken cancellationToken = default);
}
