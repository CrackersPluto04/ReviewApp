
using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Services;

/// <summary>
/// Follow relationships. Every write is idempotent: asking for a state that already holds is reported as
/// <see cref="FollowWriteStatus.Unchanged"/>, and a parallel request that wins the race on the primary key is
/// treated the same way, so retries and the offline sync can safely repeat a change.
/// </summary>
public class FollowerService : IFollowerService
{
    private readonly AppDbContext _context;
    private readonly INotificationService _notificationService;

    public FollowerService(AppDbContext context, INotificationService notificationService)
    {
        _context = context;
        _notificationService = notificationService;
    }

    public async Task<(bool Success, string Message)> FollowUserAsync(int currentUserId, int targetUserId)
    {
        switch (await SetFollowAsync(currentUserId, targetUserId, follow: true))
        {
            case FollowWriteStatus.Self: return (false, "You cannot follow yourself.");
            case FollowWriteStatus.TargetMissing: return (false, "User to follow does not exists.");
            case FollowWriteStatus.Unchanged: return (false, "You already follow this user.");
        }

        await _notificationService.NotifyNewFollowerAsync(currentUserId, targetUserId);

        return (true, "Successfully followed user.");
    }

    public async Task<(bool Success, string Message)> UnfollowUserAsync(int currentUserId, int targetUserId)
    {
        return await SetFollowAsync(currentUserId, targetUserId, follow: false) == FollowWriteStatus.Changed
            ? (true, "Successfully unfollowed user.")
            : (false, "You are not following this user.");
    }

    public async Task<(bool Success, string Message)> RemoveFollowerAsync(int currentUserId, int followerToRemoveId)
    {
        return await RemoveFollowerCoreAsync(currentUserId, followerToRemoveId) == FollowWriteStatus.Changed
            ? (true, "Successfully removed follower.")
            : (false, "This user is not following you.");
    }

    /* Core writes (also used by the offline sync): exact statuses, no notification (the caller notifies) */

    public async Task<FollowWriteStatus> SetFollowAsync(int currentUserId, int targetUserId, bool follow)
    {
        var existing = await _context.UserFollowers
            .FirstOrDefaultAsync(uf => uf.FollowerID == currentUserId && uf.FollowingID == targetUserId);

        if (!follow)
        {
            if (existing == null)
                return FollowWriteStatus.Unchanged;

            _context.UserFollowers.Remove(existing);
            await _context.SaveChangesAsync();
            return FollowWriteStatus.Changed;
        }

        if (currentUserId == targetUserId)
            return FollowWriteStatus.Self;
        if (existing != null)
            return FollowWriteStatus.Unchanged;
        if (!await _context.Users.AnyAsync(u => u.ID == targetUserId))
            return FollowWriteStatus.TargetMissing;

        var row = new UserFollower
        {
            FollowerID = currentUserId,
            FollowingID = targetUserId,
            CreatedAt = DateTime.UtcNow
        };

        _context.UserFollowers.Add(row);
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            // A parallel request followed first (primary key): already in the requested state
            _context.Entry(row).State = EntityState.Detached;
            return FollowWriteStatus.Unchanged;
        }

        return FollowWriteStatus.Changed;
    }

    public async Task<FollowWriteStatus> RemoveFollowerCoreAsync(int currentUserId, int followerToRemoveId)
    {
        var follow = await _context.UserFollowers
            .FirstOrDefaultAsync(uf => uf.FollowerID == followerToRemoveId && uf.FollowingID == currentUserId);
        if (follow == null)
            return FollowWriteStatus.Unchanged;

        _context.UserFollowers.Remove(follow);
        await _context.SaveChangesAsync();
        return FollowWriteStatus.Changed;
    }
}
