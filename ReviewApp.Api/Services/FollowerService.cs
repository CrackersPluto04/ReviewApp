
using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL;
using ReviewApp.Api.DAL.Entities;

namespace ReviewApp.Api.Services;

public class FollowerService : IFollowerService
{
    private readonly AppDbContext _context;

    public FollowerService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<(bool Success, string Message)> FollowUserAsync(int currentUserId, int targetUserId)
    {
        // Check if self-follow
        if (currentUserId == targetUserId)
            return (false, "You cannot follow yourself.");

        // Check if target exists
        var targetExists = await _context.Users.AnyAsync(u => u.ID == targetUserId);
        if (!targetExists)
            return (false, "User to follow does not exists.");

        // Check if target isnt followed already
        var alreadyFollowed = await _context.UserFollowers
            .AnyAsync(uf => uf.FollowerID == currentUserId && uf.FollowingID == targetUserId);
        if (alreadyFollowed)
            return (false, "You already follow this user.");

        // Insert follow
        var follow = new UserFollower
        {
            FollowerID = currentUserId,
            FollowingID = targetUserId,
            CreatedAt = DateTime.UtcNow
        };

        _context.UserFollowers.Add(follow);
        await _context.SaveChangesAsync();

        return (true, "Successfully followed user.");
    }

    public async Task<(bool Success, string Message)> UnfollowUserAsync(int currentUserId, int targetUserId)
    {
        // Check if follow even exists
        var follow = await _context.UserFollowers
            .FirstOrDefaultAsync(uf => uf.FollowerID == currentUserId && uf.FollowingID == targetUserId);
        if (follow == null)
            return (false, "You are not following this user.");

        // Unfollow user
        _context.UserFollowers.Remove(follow);
        await _context.SaveChangesAsync();

        return (true, "Successfully unfollowed user.");
    }

    public async Task<(bool Success, string Message)> RemoveFollowerAsync(int currentUserId, int followerToRemoveId)
    {
        // Check if follow even exists
        var follow = await _context.UserFollowers
            .FirstOrDefaultAsync(uf => uf.FollowerID == followerToRemoveId && uf.FollowingID == currentUserId);
        if (follow == null)
            return (false, "This user is not following you.");

        // Remove follower
        _context.UserFollowers.Remove(follow);
        await _context.SaveChangesAsync();

        return (true, "Successfully removed follower.");
    }
}
