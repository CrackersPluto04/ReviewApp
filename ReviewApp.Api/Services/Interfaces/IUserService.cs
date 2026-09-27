using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.DTOs;

namespace ReviewApp.Api.Services.Interfaces;

public interface IUserService
{
    Task<List<UserCompactDto>> SearchUsersAsync(string query);
    Task<UserProfileDto?> GetUserProfileAsync(string username, int? currentUserId);
    Task<(bool Success, string Message)> UpdateUserProfileAsync(int userId, UserUpdateDto userUpdateDto);
    Task<(bool Success, string Message)> ChangeEmailAsync(int userId, ChangeEmailDto dto);
    Task<(bool Success, string Message, User? User)> ChangePasswordAsync(int userId, ChangePasswordDto dto);
    Task<List<UserCompactDto>> GetUserFollowersAsync(string username, int? currentUserId);
    Task<List<UserCompactDto>> GetUserFollowingAsync(string username, int? currentUserId);
}
