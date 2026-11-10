using ReviewApp.Api.DTOs;

namespace ReviewApp.Api.Services.Interfaces;

/// <summary>
/// Reply threads under reviews, at most three layers: review, reply, reply to a reply. Replies have no visibility
/// of their own: every read and write checks the parent review's visibility, and a hidden review is reported as
/// missing.
/// </summary>
public interface IReviewReplyService
{
    /// <summary>Lists replies with keyset paging, so "load more" stays correct while replies are added or deleted.</summary>
    /// <param name="reviewId">The review whose thread is read.</param>
    /// <param name="parentReplyId">Null for the review's direct replies, or the layer-2 reply whose children are wanted.</param>
    /// <param name="afterId">Cursor: the ID of the last reply of the previous page.</param>
    /// <param name="pageSize">How many replies to return.</param>
    /// <param name="requestingUserId">The viewer, or null for an anonymous visitor.</param>
    /// <returns>The page, or null when the review does not exist or is hidden from the viewer.</returns>
    Task<ReplyPageDto?> GetRepliesAsync(int reviewId, int? parentReplyId, int? afterId, int pageSize, int? requestingUserId);

    /// <summary>Returns one reply with its layer-2 parent, for opening a thread at that reply.</summary>
    /// <returns>The context, or null when the reply does not exist, is deleted or its review is hidden from the viewer.</returns>
    Task<ReplyContextDto?> GetReplyContextAsync(int replyId, int? requestingUserId);

    /// <summary>
    /// Posts a reply and notifies whoever is being replied to. Refused for a private review, for a reply that is
    /// already at layer 3, and when the review is hidden from the author.
    /// </summary>
    /// <returns>Success, whether the review or parent was not found, a message for the client, and the created reply.</returns>
    Task<(bool Success, bool NotFound, string Message, ReplyDto? Reply)> CreateReplyAsync(int userId, CreateReplyDto dto);

    /// <summary>Soft-deletes the user's own reply: the text stays in the database but is never returned again.</summary>
    /// <returns>False when the user has no such reply.</returns>
    Task<bool> DeleteReplyAsync(int userId, int replyId);
}
