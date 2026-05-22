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

    public async Task<List<UserCompactDto>> SearchUsersAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        return await _context.Users
            .Where(u => u.Username.Contains(query))
            .Take(10)
            .Select(u => new UserCompactDto
            {
                ID = u.ID,
                Username = u.Username,
                ProfilePictureUrl = u.ProfilePictureUrl
            })
            .ToListAsync();
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

    public async Task<(bool Success, string Message)> UpdateUserProfileAsync(int userId, UserUpdateDto userUpdateDto)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
            return (false, "User not found.");

        user.Bio = userUpdateDto.Bio ?? user.Bio;
        user.ProfilePictureUrl = userUpdateDto.ProfilePictureUrl ?? user.ProfilePictureUrl;

        await _context.SaveChangesAsync();
        return (true, "Profile updated successfully.");
    }

    public async Task<List<UserCompactDto>> GetUserFollowersAsync(string username, int? currentUserId)
    {
        // Get target user
        var targetUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (targetUser == null)
            return [];

        var followers = await _context.UserFollowers
            .Where(uf => uf.FollowingID == targetUser.ID)
            .Select(uf => new UserCompactDto
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

    public async Task<List<UserCompactDto>> GetUserFollowingAsync(string username, int? currentUserId)
    {
        var targetUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (targetUser == null)
            return [];

        var following = await _context.UserFollowers
            .Where(uf => uf.FollowerID == targetUser.ID)
            .Select(uf => new UserCompactDto
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
