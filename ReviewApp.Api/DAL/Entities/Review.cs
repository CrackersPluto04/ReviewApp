using ReviewApp.Api.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ReviewApp.Api.DAL.Entities;

public class Review
{
    public int ID { get; set; }

    [Required]
    public int UserID { get; set; }
    public User? User { get; set; }

    [Required]
    public int MediaID { get; set; }
    public Media? Media { get; set; }

    [Required]
    [Column(TypeName = "decimal(3,1)")]
    public decimal Score { get; set; }

    [MaxLength(250)]
    public string? ReviewText { get; set; }
    [MaxLength(500)]
    public string? Pros { get; set; }
    [MaxLength(500)]
    public string? Cons { get; set; }

    public VisibilityLevel VisibilityLevel { get; set; } = VisibilityLevel.Private;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
