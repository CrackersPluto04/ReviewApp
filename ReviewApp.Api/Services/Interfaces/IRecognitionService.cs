using ReviewApp.Api.DTOs;

namespace ReviewApp.Api.Services.Interfaces;

/// <summary>
/// How a recognition attempt ended. The controller maps each value to an HTTP status.
/// </summary>
public enum RecognitionStatus
{
    Ok,
    /// <summary>Switched off by configuration.</summary>
    Disabled,
    /// <summary>Not a JPEG / PNG, or unreadable.</summary>
    BadImage,
    /// <summary>Over the byte or pixel limit.</summary>
    TooLarge,
    /// <summary>The user already has a recognition running.</summary>
    Busy,
    /// <summary>The daily allowance is used up.</summary>
    DailyLimit,
    /// <summary>The AI service failed. Nothing was counted against the allowance.</summary>
    ProviderUnavailable,
    /// <summary>TMDb / Spotify failed and nothing could be shown, so the recognition was refunded.</summary>
    SourceUnavailable
}

/// <summary>
/// The result of <see cref="IRecognitionService.RecognizeAsync"/>.
/// </summary>
/// <param name="Status">How it ended.</param>
/// <param name="Error">A message for the client when it did not succeed.</param>
/// <param name="Response">The result when <paramref name="Status"/> is <see cref="RecognitionStatus.Ok"/>.</param>
/// <param name="ResetsAt">When the daily allowance resets, UTC; set for <see cref="RecognitionStatus.DailyLimit"/>.</param>
public record RecognitionResult(RecognitionStatus Status, string? Error = null, RecognitionResponseDto? Response = null, DateTime? ResetsAt = null);

/// <summary>
/// Camera recognition: a photo in, a ranked list of related media out
/// (see <c>_specs/camera-object-recognition.md</c>).
/// </summary>
public interface IRecognitionService
{
    /// <summary>Whether the feature is switched on in configuration.</summary>
    bool IsEnabled { get; }

    /// <summary>Reports availability and the user's remaining daily allowance.</summary>
    Task<RecognitionAllowanceDto> GetAllowanceAsync(int userId);

    /// <summary>
    /// Recognizes a photo. Cheap checks run first (enabled, image type and size, one recognition per user at a
    /// time, daily allowance) so rejected requests never reach the paid AI call.
    /// </summary>
    /// <param name="userId">The user recognizing; the allowance and the one-at-a-time rule are per user.</param>
    /// <param name="image">The photo; it stays in memory and is dropped when the request ends.</param>
    Task<RecognitionResult> RecognizeAsync(int userId, byte[] image);

    /// <summary>Returns a further slice of a stored recognition without a new AI call.</summary>
    /// <returns>The items, or null for an unknown, expired or someone else's recognition.</returns>
    RecognitionItemsDto? GetItems(int userId, string recognitionId, int offset, int count);
}
