using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DTOs;

/// <summary>
/// Request body for posting a reply. Threads have at most three layers: review, reply, reply to a reply.
/// </summary>
public record CreateReplyDto
{
    [Required]
    public int ReviewID { get; init; }
    /// <summary>Null when replying directly to the review; otherwise a layer-2 reply.</summary>
    public int? ParentReplyID { get; init; }
    [Required, MaxLength(500)]
    public string Content { get; init; } = string.Empty;
}

/// <summary>
/// A reply as shown in a thread. A deleted reply is only listed, as a placeholder, while it still has live children.
/// </summary>
public record ReplyDto
{
    public int ID { get; init; }
    public int? ParentReplyID { get; init; }
    /// <summary>Null for deleted replies: the text stays in the database but is never returned.</summary>
    public string? Content { get; init; }
    /// <summary>Null for deleted replies, and for deleted accounts later on.</summary>
    public string? Username { get; init; }
    public string? ProfilePictureUrl { get; init; }
    public string CreatedAt { get; init; } = string.Empty;
    public bool IsDeleted { get; init; }
    /// <summary>Whether the viewer wrote this reply, so the client can offer delete.</summary>
    public bool IsOwner { get; init; }
    /// <summary>Number of non-deleted direct child replies.</summary>
    public int ChildCount { get; init; }
}

/// <summary>
/// A single reply with what is needed to show it in its thread; used by deep links from notifications.
/// </summary>
public record ReplyContextDto
{
    public int ReviewID { get; init; }
    public ReplyDto Reply { get; init; } = null!;
    /// <summary>The layer-2 reply when <see cref="Reply"/> is a layer-3 reply. Can be a deleted placeholder.</summary>
    public ReplyDto? ParentReply { get; init; }
}

/// <summary>
/// One keyset ("load more") page of replies. Keyset paging stays stable when replies are deleted between requests.
/// </summary>
public record ReplyPageDto
{
    public List<ReplyDto> Items { get; init; } = [];
    public bool HasMore { get; init; }
}
