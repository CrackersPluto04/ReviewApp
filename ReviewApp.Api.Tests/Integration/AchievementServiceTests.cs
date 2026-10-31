using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ReviewApp.Api.DAL;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services;
using ReviewApp.Api.Services.Interfaces;
using ReviewApp.Api.Tests.Support;

namespace ReviewApp.Api.Tests.Integration;

public class AchievementServiceTests : IntegrationTest
{
    private IAchievementService Achievements => Service<IAchievementService>();

    private async Task<UserAchievement?> ProgressAsync(User user, string code) =>
        await Query().UserAchievements.Include(ua => ua.Achievement).SingleOrDefaultAsync(ua => ua.UserID == user.ID && ua.Achievement.Code == code);

    [Fact]
    public async Task Evaluate_WithoutContent_CreatesNoRows()
    {
        var user = await Data.UserAsync();

        var unlocked = await Achievements.EvaluateAsync(user.ID, Enum.GetValues<AchievementMetric>());

        unlocked.ShouldBeEmpty();
        (await Query().UserAchievements.AnyAsync(ua => ua.UserID == user.ID)).ShouldBeFalse();
    }

    [Fact]
    public async Task Evaluate_UnlocksTheFirstTierAndNotifies()
    {
        var user = await Data.UserAsync();
        await Data.ReviewAsync(user, await Data.MediaAsync(MediaType.Movie));

        var unlocked = await Achievements.EvaluateAsync(user.ID, AchievementMetric.MovieReviews);

        unlocked.ShouldHaveSingleItem().ShouldBe(new UnlockedAchievementDto { GroupCode = "MOVIE_MASTER", Title = unlocked[0].Title, Tier = "Bronze" });
        var progress = await ProgressAsync(user, "MOVIE_MASTER_BRONZE");
        progress!.CurrentProgress.ShouldBe(1);
        progress.IsUnlocked.ShouldBeTrue();
        var notification = await Query().Notifications.SingleAsync(n => n.RecipientUserID == user.ID && n.Type == NotificationType.AchievementUnlocked);
        notification.GroupCode.ShouldBe("MOVIE_MASTER");
    }

    [Fact]
    public async Task Evaluate_CanUnlockSeveralTiersAtOnce()
    {
        var user = await Data.UserAsync();
        for (var i = 0; i < 10; i++)
            await Data.ReviewAsync(user, await Data.MediaAsync(MediaType.Movie));

        var unlocked = await Achievements.EvaluateAsync(user.ID, AchievementMetric.MovieReviews);

        unlocked.Select(u => u.Tier).ShouldBe(["Bronze", "Silver"]);
        (await Query().Notifications.CountAsync(n => n.RecipientUserID == user.ID && n.Type == NotificationType.AchievementUnlocked)).ShouldBe(2);
        (await ProgressAsync(user, "MOVIE_MASTER_GOLD"))!.IsUnlocked.ShouldBeFalse();
    }

    [Fact]
    public async Task Evaluate_ProgressNeverGoesDownAndUnlocksArePermanent()
    {
        var user = await Data.UserAsync();
        await Data.ReviewAsync(user, await Data.MediaAsync(MediaType.Movie));
        await Data.ReviewAsync(user, await Data.MediaAsync(MediaType.Movie));
        await Achievements.EvaluateAsync(user.ID, AchievementMetric.MovieReviews);
        await Query().Reviews.Where(r => r.UserID == user.ID).ExecuteDeleteAsync();

        var unlocked = await Achievements.EvaluateAsync(user.ID, AchievementMetric.MovieReviews);

        unlocked.ShouldBeEmpty();
        var progress = await ProgressAsync(user, "MOVIE_MASTER_BRONZE");
        progress!.CurrentProgress.ShouldBe(2);
        progress.IsUnlocked.ShouldBeTrue();
    }

    [Fact]
    public async Task Evaluate_RecreatingDeletedContent_DoesNotRaiseProgress()
    {
        var user = await Data.UserAsync();
        var media = await Data.MediaAsync(MediaType.Movie);
        await Data.ReviewAsync(user, media);
        await Achievements.EvaluateAsync(user.ID, AchievementMetric.MovieReviews);
        await Query().Reviews.Where(r => r.UserID == user.ID).ExecuteDeleteAsync();
        await Data.ReviewAsync(user, media);

        await Achievements.EvaluateAsync(user.ID, AchievementMetric.MovieReviews);

        (await ProgressAsync(user, "MOVIE_MASTER_SILVER"))!.CurrentProgress.ShouldBe(1);
    }

    [Theory]
    [InlineData(5.0, 1)]
    [InlineData(5.5, 0)]
    [InlineData(1.0, 1)]
    public async Task LowScoreReviews_CountsScoresUpToFive(double score, int expected)
    {
        var user = await Data.UserAsync();
        await Data.ReviewAsync(user, score: (decimal)score);

        await Achievements.EvaluateAsync(user.ID, AchievementMetric.LowScoreReviews);

        ((await ProgressAsync(user, "PROFESSIONAL_HATER_BRONZE"))?.CurrentProgress ?? 0).ShouldBe(expected);
    }

    [Fact]
    public async Task Replies_DeletedOnesDontCount()
    {
        var user = await Data.UserAsync();
        var review = await Data.ReviewAsync(await Data.UserAsync());
        await Data.ReplyAsync(review, user);
        await Data.ReplyAsync(review, user);
        await Data.ReplyAsync(review, user, deleted: true);

        await Achievements.EvaluateAsync(user.ID, AchievementMetric.Replies);

        (await ProgressAsync(user, "CONVERSATION_STARTER_BRONZE"))!.CurrentProgress.ShouldBe(2);
    }

    [Fact]
    public async Task CollectionsCreated_FavouritesDoesntCount()
    {
        var user = await Data.UserAsync();

        await Achievements.EvaluateAsync(user.ID, AchievementMetric.CollectionsCreated);
        (await ProgressAsync(user, "CURATOR_BRONZE")).ShouldBeNull();

        await Data.CollectionAsync(user);
        await Achievements.EvaluateAsync(user.ID, AchievementMetric.CollectionsCreated);
        (await ProgressAsync(user, "CURATOR_BRONZE"))!.IsUnlocked.ShouldBeTrue();
    }

    [Fact]
    public async Task TotalReviews_CountsEveryVisibility()
    {
        var user = await Data.UserAsync();
        await Data.ReviewAsync(user, visibility: VisibilityLevel.Public);
        await Data.ReviewAsync(user, visibility: VisibilityLevel.Private);
        await Data.ReviewAsync(user, visibility: VisibilityLevel.FollowersOnly);

        await Achievements.EvaluateAsync(user.ID, AchievementMetric.TotalReviews);

        (await ProgressAsync(user, "CRITIC_BRONZE"))!.CurrentProgress.ShouldBe(3);
    }

    [Theory]
    [InlineData(MediaType.Series, "BINGE_WATCHER_BRONZE")]
    [InlineData(MediaType.Music, "MUSIC_LOVER_BRONZE")]
    public async Task CreatingAReview_EvaluatesTheMediaTypesAchievement(MediaType type, string code)
    {
        var user = await Data.UserAsync();
        var externalId = TestData.Unique("5");

        await Service<IReviewService>().CreateReviewAsync(user.ID, new ReviewMediaDto
        {
            MediaDto = new MediaDto { ID = externalId + type, ExternalApiID = externalId, MediaType = type },
            ReviewDto = new ReviewDto { Score = 3 }
        });

        (await ProgressAsync(user, code))!.IsUnlocked.ShouldBeTrue();
        (await ProgressAsync(user, "PROFESSIONAL_HATER_BRONZE"))!.IsUnlocked.ShouldBeTrue();
        (await ProgressAsync(user, "MOVIE_MASTER_BRONZE")).ShouldBeNull();
    }

    [Fact]
    public async Task CreatingAReplyOrCollection_EvaluatesTheirAchievements()
    {
        var user = await Data.UserAsync();
        var review = await Data.ReviewAsync(await Data.UserAsync());

        await Service<IReviewReplyService>().CreateReplyAsync(user.ID, new CreateReplyDto { ReviewID = review.ID, Content = "Hi" });
        await Service<ICollectionService>().CreateCollectionAsync(user.ID, new CreateCollectionDto { Name = "Mine" });

        (await ProgressAsync(user, "CONVERSATION_STARTER_BRONZE"))!.CurrentProgress.ShouldBe(1);
        (await ProgressAsync(user, "CURATOR_BRONZE"))!.IsUnlocked.ShouldBeTrue();
    }

    [Fact]
    public async Task Evaluate_DatabaseFailure_IsSwallowed()
    {
        using var broken = TestDatabase.CreateBrokenContext();
        var service = new AchievementService(broken, [], NullLogger<AchievementService>.Instance);

        // Would throw if the failure escaped
        var unlocked = await service.EvaluateAsync(1, AchievementMetric.TotalReviews);

        unlocked.ShouldBeEmpty();
    }

    [Fact]
    public async Task Evaluate_AFailingHandlerDoesntStopTheOthers()
    {
        var user = await Data.UserAsync();
        await Data.ReviewAsync(user, await Data.MediaAsync(MediaType.Movie));
        var failing = Substitute.For<IAchievementUnlockHandler>();
        failing.HandleAsync(default, default!).ReturnsForAnyArgs<Task>(_ => throw new InvalidOperationException("boom"));
        var recording = Substitute.For<IAchievementUnlockHandler>();
        using var context = TestDatabase.CreateContext();
        var service = new AchievementService(context, [failing, recording], NullLogger<AchievementService>.Instance);

        var unlocked = await service.EvaluateAsync(user.ID, AchievementMetric.MovieReviews);

        unlocked.ShouldHaveSingleItem();
        await recording.Received(1).HandleAsync(user.ID, Arg.Is<IReadOnlyList<UnlockedAchievementDto>>(l => l.Count == 1));
    }

    [Fact]
    public async Task UserAchievements_OneCardPerGroupInSeedOrder()
    {
        var user = await Data.UserAsync();
        for (var i = 0; i < 3; i++)
            await Data.ReviewAsync(user, await Data.MediaAsync(MediaType.Movie));
        await Achievements.EvaluateAsync(user.ID, AchievementMetric.MovieReviews);

        var cards = await Achievements.GetUserAchievementsAsync(user.Username);

        cards!.Count.ShouldBe(7);
        cards.Select(c => c.GroupCode).ShouldBe(AchievementSeed.Achievements.Select(a => a.GroupCode).Distinct());
        var movies = cards[0];
        movies.CurrentProgress.ShouldBe(3);
        movies.Tiers.Select(t => (t.Tier, t.IsUnlocked)).ShouldBe([("Bronze", true), ("Silver", false), ("Gold", false)]);
        movies.Categories.ShouldNotBeEmpty();
        cards.Skip(1).ShouldAllBe(c => c.CurrentProgress == 0 && c.Tiers.All(t => !t.IsUnlocked));
    }

    [Fact]
    public async Task UserAchievements_UnknownUser_IsNull()
    {
        (await Achievements.GetUserAchievementsAsync(TestData.Unique("nobody"))).ShouldBeNull();
    }
}
