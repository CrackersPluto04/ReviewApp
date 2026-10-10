using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DTOs;

public record CreateReplyDto
{
    [Required]
    public int ReviewID { get; init; }
    // Null when replying directly to the review
    public int? ParentReplyID { get; init; }
    [Required, MaxLength(500)]
    public string Content { get; init; } = string.Empty;
}

public record ReplyDto
{
    public int ID { get; init; }
    public int? ParentReplyID { get; init; }
    // Content and author are null for deleted replies (and author for deleted accounts later on)
    public string? Content { get; init; }
    public string? Username { get; init; }
    public string? ProfilePictureUrl { get; init; }
    public string CreatedAt { get; init; } = string.Empty;
    public bool IsDeleted { get; init; }
    public bool IsOwner { get; init; }
    // Number of non-deleted direct child replies
    public int ChildCount { get; init; }
}

// A single reply with what's needed to show it in its thread (deep links from notifications)
public record ReplyContextDto
{
    public int ReviewID { get; init; }
    public ReplyDto Reply { get; init; } = null!;
    // The layer 2 reply when Reply is a layer 3 reply (can be a deleted placeholder)
    public ReplyDto? ParentReply { get; init; }
}

// Keyset ("load more") page: stays stable when replies are deleted between requests
public record ReplyPageDto
{
    public List<ReplyDto> Items { get; init; } = [];
    public bool HasMore { get; init; }
}
