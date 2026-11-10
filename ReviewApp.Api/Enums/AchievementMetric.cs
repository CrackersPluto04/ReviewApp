namespace ReviewApp.Api.Enums;

/// <summary>
/// What an achievement counts. The counting rules live in <c>AchievementService</c> (and are mirrored by the
/// backfill SQL in the <c>AddAchievements</c> migration), so keep the two in sync. Stored as integers: add new
/// metrics at the end.
/// </summary>
public enum AchievementMetric
{
    MovieReviews = 0,
    SeriesReviews = 1,
    MusicReviews = 2,
    TotalReviews = 3,
    /// <summary>Non-deleted replies written by the user.</summary>
    Replies = 4,
    /// <summary>Reviews with a score of 5.0 or lower.</summary>
    LowScoreReviews = 5,
    /// <summary>Collections except the default Favourites.</summary>
    CollectionsCreated = 6
}
