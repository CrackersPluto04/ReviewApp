using ReviewApp.Api.DTOs;

namespace ReviewApp.Api.Services.Interfaces;

public enum RecognitionStatus
{
    Ok,
    Disabled,            // switched off by configuration
    BadImage,            // not a JPEG / PNG, or unreadable
    TooLarge,            // over the byte or pixel limit
    Busy,                // the user already has a recognition running
    DailyLimit,          // the daily allowance is used up
    ProviderUnavailable, // the AI service failed, nothing was counted
    SourceUnavailable    // TMDb / Spotify failed and nothing could be shown, the recognition was refunded
}

public record RecognitionResult(RecognitionStatus Status, string? Error = null, RecognitionResponseDto? Response = null, DateTime? ResetsAt = null);

// Camera recognition: a photo in, a ranked list of related media out (see _specs/camera-object-recognition.md)
public interface IRecognitionService
{
    bool IsEnabled { get; }

    Task<RecognitionAllowanceDto> GetAllowanceAsync(int userId);

    // The photo stays in memory and is dropped when the request ends
    Task<RecognitionResult> RecognizeAsync(int userId, byte[] image);

    // The next items of a stored recognition, null for an unknown, expired or someone else's one
    RecognitionItemsDto? GetItems(int userId, string recognitionId, int offset, int count);
}
