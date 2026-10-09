using ReviewApp.Api.Enums;
using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DAL.Entities;

// One row per tier, the tiers of one achievement share the same GroupCode
public class Achievement
{
    public int ID { get; set; }

    [Required, MaxLength(50)]
    public string Code { get; set; } = string.Empty; // e.g. "MOVIE_MASTER_BRONZE"
    [Required, MaxLength(50)]
    public string GroupCode { get; set; } = string.Empty; // e.g. "MOVIE_MASTER"
    [Required, MaxLength(50)]
    public string Title { get; set; } = string.Empty;
    [Required, MaxLength(100)]
    public string Description { get; set; } = string.Empty;

    public AchievementCategories Categories { get; set; }
    public AchievementMetric Metric { get; set; }
    [Required, MaxLength(50)]
    public string IconKey { get; set; } = string.Empty; // file name of the icon in the frontend, without extension
    [Required]
    public int TargetValue { get; set; } // e.g. 10, 50, 100
    public AchievementTier Tier { get; set; }
}
