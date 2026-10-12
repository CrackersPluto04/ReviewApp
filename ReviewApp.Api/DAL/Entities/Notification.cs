using ReviewApp.Api.Enums;
using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DAL.Entities;

public class Notification
{
    public const int MaxTitleLength = 100;
    public const int MaxMessageLength = 500;

    public int ID { get; set; }

    public int RecipientUserID { get; set; }
    public User RecipientUser { get; set; } = null!;

    // Null for notifications without a human actor (system messages, achievements)
    public int? ActorUserID { get; set; }
    public User? ActorUser { get; set; }

    [Required]
    public NotificationType Type { get; set; }

    // Navigation target, which ones are set depends on the type. No foreign keys on purpose:
    // the notification outlives a deleted review / reply, and the link then shows a not found state.
    public int? ReviewID { get; set; }
    public int? ReplyID { get; set; }
    [MaxLength(50)]
    public string? GroupCode { get; set; } // achievement group

    // Stored as ready-to-display text, so it keeps reading correctly if the source changes later
    [Required, MaxLength(MaxTitleLength)]
    public string Title { get; set; } = string.Empty;
    [Required, MaxLength(MaxMessageLength)]
    public string Message { get; set; } = string.Empty;

    public bool IsSeen { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
