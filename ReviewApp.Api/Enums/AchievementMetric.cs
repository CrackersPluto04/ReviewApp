namespace ReviewApp.Api.Enums;

// What an achievement counts. The counting rules live in AchievementService (and are mirrored
// by the backfill SQL in the AddAchievements migration), so keep the two in sync.
public enum AchievementMetric
{
    MovieReviews = 0,
    SeriesReviews = 1,
    MusicReviews = 2,
    TotalReviews = 3,
    Replies = 4,          // non-deleted replies written by the user
    LowScoreReviews = 5,  // reviews with a score of 5.0 or lower
    CollectionsCreated = 6 // collections except the default "Favourites"
}
