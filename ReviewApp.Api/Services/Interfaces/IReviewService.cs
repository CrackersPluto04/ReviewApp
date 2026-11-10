using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services;

namespace ReviewApp.Api.Services.Interfaces;

/// <summary>
/// Reviews of media. There is at most one review per user and title. Visibility (private, followers only, public)
/// is decided per viewer, and a review that is hidden from the viewer is reported as missing.
/// </summary>
/// <remarks>
/// The create, edit and delete methods serve the web API, return messages and evaluate achievements. The
/// <c>*Core</c> methods do the same writes with exact statuses and stored media IDs and are shared with the
/// offline sync.
/// </remarks>
public interface IReviewService
{
    /// <summary>Lists the public reviews of one title, filtered and paged.</summary>
    Task<PagedResponse<object>> GetMediaReviewsAsync(MediaType mediaType, string externalApiId, ReviewFilterParams p);

    /// <summary>Calculates the average score and the number of reviews of one title.</summary>
    Task<(decimal AverageScore, int ReviewCount)> GetAverageScoreAsync(MediaType mediaType, string externalApiId);

    /// <summary>Lists the newest public reviews with written text, with their author and media. Feeds the Android home page.</summary>
    /// <param name="count">How many to return.</param>
    Task<List<object>> GetRecentPublicReviewsAsync(int count);

    /// <summary>Creates the user's review of a title, fetching and storing the title first if needed.</summary>
    /// <returns>Success and a message for the client; failure includes an existing review of the same title.</returns>
    Task<(bool Success, string Message)> CreateReviewAsync(int userId, ReviewMediaDto dto);

    /// <summary>Edits the user's existing review of a title.</summary>
    /// <returns>Success and a message for the client; failure includes there being no review to edit.</returns>
    Task<(bool Success, string Message)> EditReviewAsync(int userId, ReviewMediaDto dto);

    /// <summary>Deletes the user's review together with all of its replies.</summary>
    Task<(bool Success, string Message)> DeleteReviewAsync(int userId, int reviewId);

    // Core writes behind the methods above, also used by the offline sync: exact statuses, stored media IDs,
    // no achievement evaluation (the caller evaluates after committing).

    /// <summary>Core of <see cref="CreateReviewAsync"/>.</summary>
    /// <param name="userId">The review's author.</param>
    /// <param name="mediaId">The stored media ID.</param>
    /// <param name="fields">The review's score, text and visibility.</param>
    /// <param name="createdAt">The creation time to record, for a review written offline; null means now.</param>
    Task<ReviewWriteResult> CreateCoreAsync(int userId, int mediaId, ReviewFields fields, DateTime? createdAt = null);

    /// <summary>Core of <see cref="EditReviewAsync"/>.</summary>
    Task<ReviewWriteResult> UpdateCoreAsync(int userId, int reviewId, ReviewFields fields);

    /// <summary>Core of <see cref="DeleteReviewAsync"/>. Records a tombstone so the deletion reaches offline devices.</summary>
    Task<ReviewWriteResult> DeleteCoreAsync(int userId, int reviewId);

    /// <summary>Tells whether the user has reviewed a title and returns the review, so the review form can start in create or edit mode.</summary>
    Task<(bool HasReviewed, ReviewDto? Review)> CheckIfUserReviewedMediaAsync(int userId, MediaType mediaType, string externalApiId);

    /// <summary>Returns a single review with its author.</summary>
    /// <returns>The review, or null when it does not exist or is hidden from the viewer.</returns>
    Task<object?> GetReviewByIdAsync(int reviewId, int? requestingUserId);

    /// <summary>Lists a user's reviews that the viewer may see, filtered and paged.</summary>
    /// <returns>False when the user does not exist.</returns>
    Task<(bool Success, PagedResponse<object>? Reviews)> GetUserReviewsAsync(string username, int? requestingUserId, ReviewFilterParams p);
}
