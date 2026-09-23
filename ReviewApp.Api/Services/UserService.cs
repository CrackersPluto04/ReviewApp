using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Services.Interfaces;

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

        if (userUpdateDto.Username != null)
        {
            var newUsername = userUpdateDto.Username.Trim();
            if (newUsername.Length < 3 || newUsername.Length > 20)
                return (false, "Username must be between 3 and 20 characters.");

            // Exclude the user's own row so a case-only change (ben -> Ben) is allowed
            if (newUsername != user.Username &&
                await _context.Users.AnyAsync(u => u.Username == newUsername && u.ID != userId))
                return (false, "Username already taken.");

            user.Username = newUsername;
        }

        user.Bio = userUpdateDto.Bio ?? user.Bio;
        user.ProfilePictureUrl = userUpdateDto.ProfilePictureUrl ?? user.ProfilePictureUrl;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException) when (userUpdateDto.Username != null)
        {
            // Unique index violation from a concurrent request taking the same name
            return (false, "Username already taken.");
        }

        return (true, "Profile updated successfully.");
    }

    public async Task<(bool Success, string Message)> ChangeEmailAsync(int userId, ChangeEmailDto dto)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
            return (false, "User not found.");

        // Password is verified before anything else so this can't be used to probe which emails are registered
        if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash))
            return (false, "Incorrect password.");

        var newEmail = dto.NewEmail.Trim();
        if (newEmail == user.Email)
            return (false, "This is already your email address.");

        // Exclude the user's own row so a case-only change is allowed
        if (await _context.Users.AnyAsync(u => u.Email == newEmail && u.ID != userId))
            return (false, "Email already in use.");

        user.Email = newEmail;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Unique index violation from a concurrent request taking the same email
            return (false, "Email already in use.");
        }

        return (true, "Email changed successfully.");
    }

    public async Task<(bool Success, string Message, User? User)> ChangePasswordAsync(int userId, ChangePasswordDto dto)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
            return (false, "User not found.", null);

        if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash))
            return (false, "Incorrect password.", null);

        if (dto.NewPassword == dto.CurrentPassword)
            return (false, "New password must be different from the current one.", null);

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
        // Invalidates every token issued so far - the caller must issue a fresh one for the current session
        user.TokenVersion++;

        await _context.SaveChangesAsync();
        return (true, "Password changed successfully.", user);
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
