using ReviewApp.Api.DTOs;

namespace ReviewApp.Api.Services;

public interface IUserService
{
    Task<UserProfileDto?> GetUserProfileAsync(string username, int? currentUserId);
    Task<(bool Success, string Message)> UpdateUserProfileAsync(int userId, UserUpdateDto userUpdateDto);
    Task<List<UserFollowDto>> GetUserFollowersAsync(string username, int? currentUserId);
    Task<List<UserFollowDto>> GetUserFollowingAsync(string username, int? currentUserId);
}
