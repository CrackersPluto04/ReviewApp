namespace ReviewApp.Api.DAL.Entities;

/// <summary>
/// One row per recognition that reached the AI provider, for the daily allowance (the rows since UTC midnight are
/// the count). Holds no photo, no answer and nothing recognised, only who and when. The row is removed again when a
/// TMDb / Spotify outage left nothing to show, and the daily cleanup drops rows older than a week.
/// </summary>
public class RecognitionUsage
{
    public int ID { get; set; }

    public int UserID { get; set; }
    public User User { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
