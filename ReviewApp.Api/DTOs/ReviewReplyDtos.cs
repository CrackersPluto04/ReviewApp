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
    public DateTime CreatedAt { get; init; }
    public bool IsDeleted { get; init; }
    public bool IsOwner { get; init; }
    // Number of non-deleted direct child replies
    public int ChildCount { get; init; }
}

// Keyset ("load more") page: stays stable when replies are deleted between requests
public record ReplyPageDto
{
    public List<ReplyDto> Items { get; init; } = [];
    public bool HasMore { get; init; }
}
