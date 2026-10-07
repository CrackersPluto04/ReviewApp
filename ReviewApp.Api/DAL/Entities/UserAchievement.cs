namespace ReviewApp.Api.DAL.Entities;

public class UserAchievement
{
    public int UserID { get; set; }
    public User User { get; set; } = null!;

    public int AchievementID { get; set; }
    public Achievement Achievement { get; set; } = null!;

    public int CurrentProgress { get; set; }
    public bool IsUnlocked { get; set; }
    public DateTime? UnlockedAt { get; set; }
}
