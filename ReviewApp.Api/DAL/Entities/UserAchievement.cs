namespace ReviewApp.Api.DAL.Entities;

public class UserAchievement
{
    public int UserID { get; set; }
    public User User { get; set; } = null!;

    public int AchievementID { get; set; }
    public Achievement Achievement { get; set; } = null!;

    // Recalculated from the real counts, can go down when content is deleted
    public int CurrentProgress { get; set; }
    // Permanent once set, never flips back
    public bool IsUnlocked { get; set; }
}
