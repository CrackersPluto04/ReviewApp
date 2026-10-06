using ReviewApp.Api.DTOs;

namespace ReviewApp.Api.Services.Interfaces;

public interface IReviewReplyService
{
    Task<ReplyPageDto?> GetRepliesAsync(int reviewId, int? parentReplyId, int? afterId, int pageSize, int? requestingUserId);
    Task<(bool Success, bool NotFound, string Message, ReplyDto? Reply)> CreateReplyAsync(int userId, CreateReplyDto dto);
    Task<bool> DeleteReplyAsync(int userId, int replyId);
}
