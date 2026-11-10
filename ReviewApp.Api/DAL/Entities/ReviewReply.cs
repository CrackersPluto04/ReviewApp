using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DAL.Entities;

/// <summary>
/// A reply in a review's thread: to the review itself (<see cref="ParentReplyID"/> null) or to such a reply. The
/// layer is derived from the parent chain and the service stops at three layers.
/// </summary>
public class ReviewReply
{
    public int ID { get; set; }

    /// <summary>The review the thread belongs to. Replies are deleted with it.</summary>
    public int ReviewID { get; set; }
    public Review Review { get; set; } = null!;

    /// <summary>The author. Deleting a user is restricted while replies exist, to avoid multiple cascade paths.</summary>
    public int UserID { get; set; }
    public User User { get; set; } = null!;

    /// <summary>Self-referencing relationship for nesting; restricted on delete to avoid cycles.</summary>
    public int? ParentReplyID { get; set; }
    public ReviewReply? ParentReply { get; set; }
    public ICollection<ReviewReply> ChildReplies { get; set; } = [];

    [MaxLength(500)]
    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Soft delete: the record (and its content) stays so child replies are not orphaned, but it is never returned.</summary>
    public bool IsDeleted { get; set; } = false;
}
