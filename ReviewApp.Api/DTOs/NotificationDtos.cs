using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DTOs;

/// <summary>
/// A notification as shown to its recipient. Title and message are stored ready to display, and the navigation
/// target is kept as plain values without foreign keys, so the notification outlives the content it points to.
/// </summary>
public record NotificationDto
{
    public int ID { get; init; }
    /// <summary>The name of the notification type, e.g. <c>ReviewReply</c> or <c>NewFollower</c>. Clients map unknown names to a system notification.</summary>
    public string Type { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public bool IsSeen { get; init; }
    /// <summary>Creation time, UTC.</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>Username of whoever triggered it; null for system and achievement notifications.</summary>
    public string? ActorUsername { get; init; }
    public string? ActorProfilePictureUrl { get; init; }

    /// <summary>Navigation target: the clients build the route from <see cref="Type"/> and these three values. A target that was deleted since then leads to a not-found state.</summary>
    public int? ReviewID { get; init; }
    public int? ReplyID { get; init; }
    public string? GroupCode { get; init; }
}

/// <summary>
/// One keyset ("load more") page of notifications.
/// </summary>
public record NotificationPageDto
{
    public List<NotificationDto> Items { get; init; } = [];
    public bool HasMore { get; init; }
    /// <summary>Notifications matching the current filter.</summary>
    public int TotalCount { get; init; }
    /// <summary>Unseen notifications overall, whatever the filter; feeds the bell badge.</summary>
    public int UnseenCount { get; init; }
}

/// <summary>
/// Query-string parameters of the notification list.
/// </summary>
public class NotificationQueryParams
{
    /// <summary><c>all</c>, <c>seen</c> or <c>unseen</c>.</summary>
    public string Status { get; set; } = "all";
    /// <summary><c>newest</c> or <c>oldest</c>.</summary>
    public string Sort { get; set; } = "newest";
    /// <summary>Keyset cursor: the ID of the last notification of the previous page.</summary>
    public int? AfterId { get; set; }
    [Range(1, 50)]
    public int PageSize { get; set; } = 20;
}

/// <summary>
/// Request body of the seen / unseen toggle.
/// </summary>
public record SetNotificationSeenDto
{
    /// <summary>
    /// Nullable so <c>[Required]</c> can reject a missing value; a plain <c>bool</c> would silently become
    /// <c>false</c> (unseen).
    /// </summary>
    [Required]
    public bool? IsSeen { get; init; }
}
