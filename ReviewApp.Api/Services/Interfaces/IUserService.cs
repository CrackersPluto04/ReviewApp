using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.DTOs;

namespace ReviewApp.Api.Services.Interfaces;

/// <summary>
/// User profiles, search, follower lists and the profile and password changes of the logged-in user.
/// </summary>
public interface IUserService
{
    /// <summary>Searches users by username.</summary>
    Task<List<UserCompactDto>> SearchUsersAsync(string query);

    /// <summary>Returns a user's public profile as seen by one viewer.</summary>
    /// <param name="username">The profile owner's username.</param>
    /// <param name="currentUserId">The viewer, or null for an anonymous visitor.</param>
    /// <returns>The profile, or null if the user does not exist.</returns>
    Task<UserProfileDto?> GetUserProfileAsync(string username, int? currentUserId);

    /// <summary>Applies the fields that were sent (username, bio, picture) to the user's profile.</summary>
    /// <returns>Success and a message for the client.</returns>
    Task<(bool Success, string Message)> UpdateUserProfileAsync(int userId, UserUpdateDto userUpdateDto);

    /// <summary>
    /// Changes the password after checking the current one, and revokes every other session by bumping <c>TokenVersion</c>.
    /// </summary>
    /// <returns>Success, a message, and the updated user so the caller can re-issue a session for this browser.</returns>
    Task<(bool Success, string Message, User? User)> ChangePasswordAsync(int userId, ChangePasswordDto dto);

    /// <summary>Lists the users who follow the given user.</summary>
    /// <returns>The followers, or null if the user does not exist.</returns>
    Task<List<UserCompactDto>?> GetUserFollowersAsync(string username, int? currentUserId);

    /// <summary>Lists the users the given user follows.</summary>
    /// <returns>The followed users, or null if the user does not exist.</returns>
    Task<List<UserCompactDto>?> GetUserFollowingAsync(string username, int? currentUserId);
}
