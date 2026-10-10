using ReviewApp.Api.DTOs;

namespace ReviewApp.Api.Services.Interfaces;

public interface IReviewReplyService
{
    Task<ReplyPageDto?> GetRepliesAsync(int reviewId, int? parentReplyId, int? afterId, int pageSize, int? requestingUserId);
    // Null when the reply doesn't exist, is deleted or its review is hidden from the requesting user
    Task<ReplyContextDto?> GetReplyContextAsync(int replyId, int? requestingUserId);
    Task<(bool Success, bool NotFound, string Message, ReplyDto? Reply)> CreateReplyAsync(int userId, CreateReplyDto dto);
    Task<bool> DeleteReplyAsync(int userId, int replyId);
}
