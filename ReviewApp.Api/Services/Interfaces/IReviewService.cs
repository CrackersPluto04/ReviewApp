using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;

namespace ReviewApp.Api.Services.Interfaces;

public interface IReviewService
{
    Task<PagedResponse<object>> GetMediaReviewsAsync(MediaType mediaType, string externalApiId, ReviewFilterParams p);
    Task<(decimal AverageScore, int ReviewCount)> GetAverageScoreAsync(MediaType mediaType, string externalApiId);

    Task<(bool Success, string Message)> CreateReviewAsync(int userId, ReviewMediaDto dto);
    Task<(bool Success, string Message)> EditReviewAsync(int userId, ReviewMediaDto dto);
    Task<(bool Success, string Message)> DeleteReviewAsync(int userId, int reviewId);

    Task<(bool HasReviewed, ReviewDto? Review)> CheckIfUserReviewedMediaAsync(int userId, MediaType mediaType, string externalApiId);
    Task<(bool Success, PagedResponse<object>? Reviews)> GetUserReviewsAsync(string username, int? requestingUserId, ReviewFilterParams p);
}
