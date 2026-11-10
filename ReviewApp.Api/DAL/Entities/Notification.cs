using ReviewApp.Api.Enums;
using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DAL.Entities;

/// <summary>
/// An in-app notification. Title and message are stored ready to display, and the navigation target is stored as
/// plain values without foreign keys, so a notification outlives the content it points to. A user keeps at most
/// 200 of them.
/// </summary>
public class Notification
{
    public const int MaxTitleLength = 100;
    public const int MaxMessageLength = 500;

    public int ID { get; set; }

    /// <summary>The user who receives it. Notifications are removed together with the recipient.</summary>
    public int RecipientUserID { get; set; }
    public User RecipientUser { get; set; } = null!;

    /// <summary>The user who triggered it. Null for notifications without a human actor (system messages, achievements).</summary>
    public int? ActorUserID { get; set; }
    public User? ActorUser { get; set; }

    [Required]
    public NotificationType Type { get; set; }

    /// <summary>
    /// Navigation target; which of <see cref="ReviewID"/>, <see cref="ReplyID"/> and <see cref="GroupCode"/> are set
    /// depends on the type. No foreign keys on purpose: the notification outlives a deleted review or reply, and the
    /// link then shows a not-found state.
    /// </summary>
    public int? ReviewID { get; set; }
    public int? ReplyID { get; set; }
    /// <summary>The achievement group of an unlock notification.</summary>
    [MaxLength(50)]
    public string? GroupCode { get; set; }

    /// <summary>Stored as ready-to-display text, so it keeps reading correctly if the source changes later.</summary>
    [Required, MaxLength(MaxTitleLength)]
    public string Title { get; set; } = string.Empty;
    [Required, MaxLength(MaxMessageLength)]
    public string Message { get; set; } = string.Empty;

    public bool IsSeen { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
