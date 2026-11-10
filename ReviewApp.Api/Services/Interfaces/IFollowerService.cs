using ReviewApp.Api.Services;

namespace ReviewApp.Api.Services.Interfaces;

/// <summary>
/// Follow relationships between users. The first group of methods serves the web API and returns messages; the
/// core methods beneath them are shared with the offline sync.
/// </summary>
public interface IFollowerService
{
    /// <summary>Makes <paramref name="currentUserId"/> follow another user and notifies the target.</summary>
    /// <returns>Success and a message for the client. Failure covers following oneself, an unknown user and an existing follow.</returns>
    Task<(bool Success, string Message)> FollowUserAsync(int currentUserId, int targetUserId);

    /// <summary>Stops <paramref name="currentUserId"/> from following another user.</summary>
    Task<(bool Success, string Message)> UnfollowUserAsync(int currentUserId, int targetUserId);

    /// <summary>Removes someone from the current user's followers.</summary>
    Task<(bool Success, string Message)> RemoveFollowerAsync(int currentUserId, int followerToRemoveId);

    // Core writes behind the methods above, also used by the offline sync: idempotent, exact statuses,
    // no new-follower notification (the caller notifies after committing).

    /// <summary>Follows or unfollows. Idempotent: a state that already holds is reported as <c>Unchanged</c>.</summary>
    /// <param name="currentUserId">The user who follows or unfollows.</param>
    /// <param name="targetUserId">The user being followed or unfollowed.</param>
    /// <param name="follow">True to follow, false to unfollow.</param>
    Task<FollowWriteStatus> SetFollowAsync(int currentUserId, int targetUserId, bool follow);

    /// <summary>Core of <see cref="RemoveFollowerAsync"/>.</summary>
    Task<FollowWriteStatus> RemoveFollowerCoreAsync(int currentUserId, int followerToRemoveId);
}
