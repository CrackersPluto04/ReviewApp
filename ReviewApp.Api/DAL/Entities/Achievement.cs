using ReviewApp.Api.Enums;
using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DAL.Entities;

/// <summary>
/// An achievement definition, seeded from <see cref="AchievementSeed"/>. One row per tier: the tiers of one
/// achievement card share the same <see cref="GroupCode"/> and <see cref="Metric"/>.
/// </summary>
public class Achievement
{
    public int ID { get; set; }

    /// <summary>Unique code of this tier, e.g. <c>MOVIE_MASTER_BRONZE</c>.</summary>
    [Required, MaxLength(50)]
    public string Code { get; set; } = string.Empty;
    /// <summary>Shared by all tiers of one card, e.g. <c>MOVIE_MASTER</c>.</summary>
    [Required, MaxLength(50)]
    public string GroupCode { get; set; } = string.Empty;
    [Required, MaxLength(50)]
    public string Title { get; set; } = string.Empty;
    [Required, MaxLength(100)]
    public string Description { get; set; } = string.Empty;

    public AchievementCategories Categories { get; set; }
    /// <summary>What is counted towards <see cref="TargetValue"/>.</summary>
    public AchievementMetric Metric { get; set; }
    /// <summary>File name of the icon in the frontend, without the extension.</summary>
    [Required, MaxLength(50)]
    public string IconKey { get; set; } = string.Empty;
    /// <summary>The count that unlocks this tier, e.g. 10, 50 or 100.</summary>
    [Required]
    public int TargetValue { get; set; }
    public AchievementTier Tier { get; set; }
}
