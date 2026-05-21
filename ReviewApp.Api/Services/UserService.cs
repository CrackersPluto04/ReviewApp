using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL;
using ReviewApp.Api.DTOs;

namespace ReviewApp.Api.Services;

public class UserService : IUserService
{
    private readonly AppDbContext _context;

    public UserService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<UserProfileDto?> GetUserProfileAsync(string username, int? currentUserId)
    {
        // Get target user
        var targetUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (targetUser == null)
            return null;

        // Check if owner
        var isOwner = currentUserId.HasValue && currentUserId == targetUser.ID;

        // Get followers and following counts
        var followersCount = await _context.UserFollowers.CountAsync(uf => uf.FollowingID == targetUser.ID);
        var followingCount = await _context.UserFollowers.CountAsync(uf => uf.FollowerID == targetUser.ID);

        // Get profile informations
        return new UserProfileDto
        {
            ID = targetUser.ID,
            Username = username,
            Bio = targetUser.Bio,
            ProfilePictureUrl = targetUser.ProfilePictureUrl,
            CreatedAt = targetUser.CreatedAt.ToString("yyyy-MM-dd"),

            FollowersCount = followersCount,
            FollowingCount = followingCount,
            IsFollowedByCurrentUser = currentUserId.HasValue && !isOwner &&
                _context.UserFollowers.Any(check =>
                    check.FollowerID == currentUserId.Value &&
                    check.FollowingID == targetUser.ID)
        };
    }

    public async Task<List<UserFollowDto>> GetUserFollowersAsync(string username, int? currentUserId)
    {
        // Get target user
        var targetUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (targetUser == null)
            return [];

        var followers = await _context.UserFollowers
            .Where(uf => uf.FollowingID == targetUser.ID)
            .Select(uf => new UserFollowDto
            {
                ID = uf.FollowerID,
                Username = uf.Follower.Username,
                ProfilePictureUrl = uf.Follower.ProfilePictureUrl,

                IsFollowedByCurrentUser = currentUserId.HasValue &&
                    _context.UserFollowers.Any(check =>
                        check.FollowerID == currentUserId.Value &&
                        check.FollowingID == uf.FollowerID)
            })
            .ToListAsync();

        return followers;
    }

    public async Task<List<UserFollowDto>> GetUserFollowingAsync(string username, int? currentUserId)
    {
        var targetUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (targetUser == null)
            return [];

        var following = await _context.UserFollowers
            .Where(uf => uf.FollowerID == targetUser.ID)
            .Select(uf => new UserFollowDto
            {
                ID = uf.FollowingID,
                Username = uf.Following.Username,
                ProfilePictureUrl = uf.Following.ProfilePictureUrl,

                IsFollowedByCurrentUser = currentUserId.HasValue &&
                    _context.UserFollowers.Any(check =>
                        check.FollowerID == currentUserId.Value &&
                        check.FollowingID == uf.FollowingID)
            })
            .ToListAsync();

        return following;
    }
}
