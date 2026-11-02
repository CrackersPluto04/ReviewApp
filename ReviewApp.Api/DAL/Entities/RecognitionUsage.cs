namespace ReviewApp.Api.DAL.Entities;

// One row per recognition that reached the AI provider, for the daily allowance.
// Holds no photo, no answer and nothing recognised, only who and when.
public class RecognitionUsage
{
    public int ID { get; set; }

    public int UserID { get; set; }
    public User User { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
