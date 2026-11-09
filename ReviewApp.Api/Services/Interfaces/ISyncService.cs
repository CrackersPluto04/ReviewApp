using ReviewApp.Api.DTOs;

namespace ReviewApp.Api.Services.Interfaces;

public interface ISyncService
{
    // Applies the device's queued changes in order, each on its own, and answers each one (Services/Sync/SyncService.cs)
    Task<SyncPushResponse> PushAsync(int userId, IReadOnlyList<SyncOpDto> ops, CancellationToken cancellationToken = default);
}
