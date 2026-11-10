namespace ReviewApp.Api.DAL.Entities;

/// <summary>
/// A user's progress on one achievement tier. A missing row means zero progress, so rows are only created once
/// there is something to record. Removed together with the user or the definition.
/// </summary>
public class UserAchievement
{
    public int UserID { get; set; }
    public User User { get; set; } = null!;

    public int AchievementID { get; set; }
    public Achievement Achievement { get; set; } = null!;

    /// <summary>The highest count ever reached: recounted from the real totals but never lowered, so deleting content takes nothing away.</summary>
    public int CurrentProgress { get; set; }
    /// <summary>Permanent once set, never flips back.</summary>
    public bool IsUnlocked { get; set; }
}
