using ReviewApp.Api.Enums;
using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DAL.Entities;

public class Notification
{
    public int ID { get; set; }

    public int RecipientUserID { get; set; }
    public User RecipientUser { get; set; } = null!;

    public int ActorUserID { get; set; }
    public User ActorUser { get; set; } = null!;

    [Required]
    public NotificationType Type { get; set; } // System, ReviewReply, AchievementUnlocked, ...
    public int TargetID { get; set; }          // e.g., ReviewID
    [MaxLength(500)]
    public string Message { get; set; } = string.Empty;
    public bool IsRead { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
