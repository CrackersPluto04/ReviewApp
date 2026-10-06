using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DAL.Entities;

public class ReviewReply
{
    public int ID { get; set; }

    // Parent Review
    public int ReviewID { get; set; }
    public Review Review { get; set; } = null!;

    // Author
    public int UserID { get; set; }
    public User User { get; set; } = null!;

    // Self-referencing relationship for nesting
    public int? ParentReplyID { get; set; }
    public ReviewReply? ParentReply { get; set; }
    public ICollection<ReviewReply> ChildReplies { get; set; } = [];

    [MaxLength(500)]
    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Soft delete: Keeps the record after delete so child replies aren't orphaned
    public bool IsDeleted { get; set; } = false;
}
