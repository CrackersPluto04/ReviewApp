namespace ReviewApp.Api.Services.Interfaces;

public interface IFollowerService
{
    Task<(bool Success, string Message)> FollowUserAsync(int currentUserId, int targetUserId);
    Task<(bool Success, string Message)> UnfollowUserAsync(int currentUserId, int targetUserId);
    Task<(bool Success, string Message)> RemoveFollowerAsync(int currentUserId, int followerToRemoveId);
}
