using ReviewApp.Api.DTOs;

namespace ReviewApp.Api.Services.Interfaces;

/// <summary>
/// The push half of the Android offline sync (see <c>Services/Sync/SyncService.cs</c> for the algorithm).
/// </summary>
public interface ISyncService
{
    /// <summary>
    /// Applies the device's queued changes in order, each on its own, and answers each one. A change that was
    /// already applied (same operation ID) is answered from its stored result instead of being applied twice.
    /// </summary>
    /// <param name="userId">The logged-in user, who owns every change.</param>
    /// <param name="ops">The queued changes, in the order the user made them.</param>
    /// <param name="cancellationToken">Cancelled when the client disconnects.</param>
    Task<SyncPushResponse> PushAsync(int userId, IReadOnlyList<SyncOpDto> ops, CancellationToken cancellationToken = default);
}
