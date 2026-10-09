using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.Enums;

namespace ReviewApp.Api.DAL;

// Achievement definitions, seeded with HasData. IDs must stay stable once migrated:
// add new tiers with new IDs and a new migration instead of renumbering.
public static class AchievementSeed
{
    public static readonly Achievement[] Achievements =
    [
        // Movie Master
        Tier(1, "MOVIE_MASTER", "Movie Master", "Review 1 movie.", AchievementCategories.Movie | AchievementCategories.Review, AchievementMetric.MovieReviews, "movie-master", 1, AchievementTier.Bronze),
        Tier(2, "MOVIE_MASTER", "Movie Master", "Review 10 movies.", AchievementCategories.Movie | AchievementCategories.Review, AchievementMetric.MovieReviews, "movie-master", 10, AchievementTier.Silver),
        Tier(3, "MOVIE_MASTER", "Movie Master", "Review 50 movies.", AchievementCategories.Movie | AchievementCategories.Review, AchievementMetric.MovieReviews, "movie-master", 50, AchievementTier.Gold),

        // Binge Watcher
        Tier(4, "BINGE_WATCHER", "Binge Watcher", "Review 1 series.", AchievementCategories.Series | AchievementCategories.Review, AchievementMetric.SeriesReviews, "binge-watcher", 1, AchievementTier.Bronze),
        Tier(5, "BINGE_WATCHER", "Binge Watcher", "Review 10 series.", AchievementCategories.Series | AchievementCategories.Review, AchievementMetric.SeriesReviews, "binge-watcher", 10, AchievementTier.Silver),
        Tier(6, "BINGE_WATCHER", "Binge Watcher", "Review 50 series.", AchievementCategories.Series | AchievementCategories.Review, AchievementMetric.SeriesReviews, "binge-watcher", 50, AchievementTier.Gold),

        // Music Lover
        Tier(7, "MUSIC_LOVER", "Music Lover", "Review 1 piece of music.", AchievementCategories.Music | AchievementCategories.Review, AchievementMetric.MusicReviews, "music-lover", 1, AchievementTier.Bronze),
        Tier(8, "MUSIC_LOVER", "Music Lover", "Review 10 pieces of music.", AchievementCategories.Music | AchievementCategories.Review, AchievementMetric.MusicReviews, "music-lover", 10, AchievementTier.Silver),
        Tier(9, "MUSIC_LOVER", "Music Lover", "Review 50 pieces of music.", AchievementCategories.Music | AchievementCategories.Review, AchievementMetric.MusicReviews, "music-lover", 50, AchievementTier.Gold),

        // Critic
        Tier(10, "CRITIC", "Critic", "Write 10 reviews.", AchievementCategories.Review, AchievementMetric.TotalReviews, "critic", 10, AchievementTier.Bronze),
        Tier(11, "CRITIC", "Critic", "Write 50 reviews.", AchievementCategories.Review, AchievementMetric.TotalReviews, "critic", 50, AchievementTier.Silver),
        Tier(12, "CRITIC", "Critic", "Write 100 reviews.", AchievementCategories.Review, AchievementMetric.TotalReviews, "critic", 100, AchievementTier.Gold),

        // Conversation Starter
        Tier(13, "CONVERSATION_STARTER", "Conversation Starter", "Write 10 replies.", AchievementCategories.Reply, AchievementMetric.Replies, "conversation-starter", 10, AchievementTier.Bronze),
        Tier(14, "CONVERSATION_STARTER", "Conversation Starter", "Write 50 replies.", AchievementCategories.Reply, AchievementMetric.Replies, "conversation-starter", 50, AchievementTier.Silver),
        Tier(15, "CONVERSATION_STARTER", "Conversation Starter", "Write 100 replies.", AchievementCategories.Reply, AchievementMetric.Replies, "conversation-starter", 100, AchievementTier.Gold),

        // Professional Hater
        Tier(16, "PROFESSIONAL_HATER", "Professional Hater", "Have 1 review scored 5.0 or less.", AchievementCategories.Review, AchievementMetric.LowScoreReviews, "professional-hater", 1, AchievementTier.Bronze),
        Tier(17, "PROFESSIONAL_HATER", "Professional Hater", "Have 10 reviews scored 5.0 or less.", AchievementCategories.Review, AchievementMetric.LowScoreReviews, "professional-hater", 10, AchievementTier.Silver),
        Tier(18, "PROFESSIONAL_HATER", "Professional Hater", "Have 50 reviews scored 5.0 or less.", AchievementCategories.Review, AchievementMetric.LowScoreReviews, "professional-hater", 50, AchievementTier.Gold),

        // Curator
        Tier(19, "CURATOR", "Curator", "Create 1 collection of your own.", AchievementCategories.Collection, AchievementMetric.CollectionsCreated, "curator", 1, AchievementTier.Bronze),
        Tier(20, "CURATOR", "Curator", "Create 5 collections of your own.", AchievementCategories.Collection, AchievementMetric.CollectionsCreated, "curator", 5, AchievementTier.Silver),
        Tier(21, "CURATOR", "Curator", "Create 10 collections of your own.", AchievementCategories.Collection, AchievementMetric.CollectionsCreated, "curator", 10, AchievementTier.Gold),
    ];

    private static Achievement Tier(int id, string groupCode, string title, string description,
        AchievementCategories categories, AchievementMetric metric, string iconKey, int target, AchievementTier tier) => new()
    {
        ID = id,
        Code = $"{groupCode}_{tier.ToString().ToUpperInvariant()}",
        GroupCode = groupCode,
        Title = title,
        Description = description,
        Categories = categories,
        Metric = metric,
        IconKey = iconKey,
        TargetValue = target,
        Tier = tier
    };
}
