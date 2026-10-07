using ReviewApp.Api.Enums;
using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DAL.Entities;

public class Achievement
{
    public int ID { get; set; }

    [Required, MaxLength(50)]
    public string Code { get; set; } = string.Empty; // e.g. "REVIEWS_10", "MOVIE_100"
    [Required, MaxLength(50)]
    public string Title { get; set; } = string.Empty;
    [Required, MaxLength(100)]
    public string Description { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public AchievementCategory Category { get; set; } // "Movies", "Social", "Collections"
    public string IconUrl { get; set; } = string.Empty;
    [Required]
    public int TargetValue { get; set; } // e.g. 10, 50, 100
    public AchievementTier Tier { get; set; }
}
