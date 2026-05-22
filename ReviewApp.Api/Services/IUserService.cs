using ReviewApp.Api.DTOs;

namespace ReviewApp.Api.Services;

public interface IUserService
{
    Task<List<UserCompactDto>> SearchUsersAsync(string query);
    Task<UserProfileDto?> GetUserProfileAsync(string username, int? currentUserId);
    Task<(bool Success, string Message)> UpdateUserProfileAsync(int userId, UserUpdateDto userUpdateDto);
    Task<List<UserCompactDto>> GetUserFollowersAsync(string username, int? currentUserId);
    Task<List<UserCompactDto>> GetUserFollowingAsync(string username, int? currentUserId);
}
