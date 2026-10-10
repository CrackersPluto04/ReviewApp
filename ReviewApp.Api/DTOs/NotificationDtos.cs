using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DTOs;

public record NotificationDto
{
    public int ID { get; init; }
    public string Type { get; init; } = string.Empty; // System, ReviewReply, ReplyReply, AchievementUnlocked, NewFollower
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public bool IsSeen { get; init; }
    public DateTime CreatedAt { get; init; } // UTC

    // Who triggered it, null for system / achievement notifications
    public string? ActorUsername { get; init; }
    public string? ActorProfilePictureUrl { get; init; }

    // Navigation target, the frontend builds the route from the type and these
    public int? ReviewID { get; init; }
    public int? ReplyID { get; init; }
    public string? GroupCode { get; init; }
}

// Keyset ("load more") page
public record NotificationPageDto
{
    public List<NotificationDto> Items { get; init; } = [];
    public bool HasMore { get; init; }
    public int TotalCount { get; init; }  // matching the current filter
    public int UnseenCount { get; init; } // overall, for the badge
}

public class NotificationQueryParams
{
    public string Status { get; set; } = "all"; // all, seen, unseen
    public string Sort { get; set; } = "newest"; // newest, oldest
    public int? AfterId { get; set; }
    [Range(1, 50)]
    public int PageSize { get; set; } = 20;
}

public record SetNotificationSeenDto
{
    [Required]
    public bool IsSeen { get; init; }
}
