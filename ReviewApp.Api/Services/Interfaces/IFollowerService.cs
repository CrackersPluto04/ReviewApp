using ReviewApp.Api.Services;

namespace ReviewApp.Api.Services.Interfaces;

public interface IFollowerService
{
    Task<(bool Success, string Message)> FollowUserAsync(int currentUserId, int targetUserId);
    Task<(bool Success, string Message)> UnfollowUserAsync(int currentUserId, int targetUserId);
    Task<(bool Success, string Message)> RemoveFollowerAsync(int currentUserId, int followerToRemoveId);

    // Core writes behind the methods above, also used by the offline sync: idempotent, exact statuses,
    // no new-follower notification (the caller notifies after committing)
    Task<FollowWriteStatus> SetFollowAsync(int currentUserId, int targetUserId, bool follow);
    Task<FollowWriteStatus> RemoveFollowerCoreAsync(int currentUserId, int followerToRemoveId);
}
