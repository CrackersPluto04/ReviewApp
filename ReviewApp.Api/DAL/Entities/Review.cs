using ReviewApp.Api.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ReviewApp.Api.DAL.Entities;

/// <summary>
/// A user's review of one media item; at most one per user and media (a unique index).
/// </summary>
public class Review
{
    public int ID { get; set; }

    [Required]
    public int UserID { get; set; }
    public User User { get; set; } = null!;

    [Required]
    public int MediaID { get; set; }
    public Media Media { get; set; } = null!;

    /// <summary>1.0 to 10.0 with one decimal.</summary>
    [Required]
    [Column(TypeName = "decimal(3,1)")]
    public decimal Score { get; set; }

    [MaxLength(500)]
    public string? ReviewText { get; set; }
    [MaxLength(250)]
    public string? Pros { get; set; }
    [MaxLength(250)]
    public string? Cons { get; set; }

    public VisibilityLevel VisibilityLevel { get; set; } = VisibilityLevel.Private;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Maintained by the <c>SetUpdatedAt</c> database trigger, never set from application code.</summary>
    [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// SQL Server <c>rowversion</c>: changes on every write of the row (the <c>SetUpdatedAt</c> trigger's own update
    /// included). It is the base version an offline edit is checked against and the cursor of the sync's changes
    /// feed. Deliberately not a concurrency token, so web edits keep "last write wins".
    /// </summary>
    public long RowVersion { get; set; }
}
