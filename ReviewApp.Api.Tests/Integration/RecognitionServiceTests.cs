using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.Services.Interfaces;
using ReviewApp.Api.Services.Recognition;
using ReviewApp.Api.Tests.Support;

namespace ReviewApp.Api.Tests.Integration;

// The recognition pipeline from the app's container against the test database, with the fake AI provider and media sources
public class RecognitionServiceTests : IntegrationTest
{
    private IRecognitionService Recognition => Service<IRecognitionService>();
    private TestRecognitionProvider Provider => App.Recognition;

    private static string Tag() => TestData.Unique("img");

    private async Task<User> PremiumUserAsync()
    {
        var user = await Data.UserAsync();
        await Data.PremiumAsync(user);
        return user;
    }

    private Task<int> UsageCountAsync(User user) => Query().RecognitionUsages.CountAsync(u => u.UserID == user.ID);

    [Fact]
    public async Task Photo_GivesTheFirstTenRankedItems_AndCountsOnce()
    {
        var user = await PremiumUserAsync();
        var tag = Tag();

        var result = await Recognition.RecognizeAsync(user.ID, TestImages.Jpeg(tag));

        result.Status.ShouldBe(RecognitionStatus.Ok);
        var response = result.Response!;
        response.Outcome.ShouldBe("recognised");
        response.Summary.ShouldBe($"We saw {tag}.");
        response.Subject!.Label.ShouldBe($"{tag} figure");
        response.Items.Count.ShouldBe(10);
        response.Total.ShouldBe(12);
        response.HasMore.ShouldBeTrue();
        response.NextOffset.ShouldBe(10);
        response.RecognitionId.ShouldNotBeNullOrEmpty();
        response.Incomplete.ShouldBeFalse();
        // The object's own works first
        response.Items.Take(3).ShouldAllBe(i => i.Tier == "direct");
        response.Items.Skip(3).ShouldAllBe(i => i.Tier == "universe");
        response.Items[0].Media.Title.ShouldStartWith(tag);
        response.Allowance.ShouldSatisfyAllConditions(
            a => a.Limit.ShouldBe(20),
            a => a.Remaining.ShouldBe(19),
            a => a.ResetsAt.ShouldBe(DateTime.UtcNow.Date.AddDays(1)));
        (await UsageCountAsync(user)).ShouldBe(1);
    }

    [Fact]
    public async Task ShowMore_ContinuesTheSameList_WithoutANewAiCall()
    {
        var user = await PremiumUserAsync();
        var tag = Tag();
        var first = (await Recognition.RecognizeAsync(user.ID, TestImages.Jpeg(tag))).Response!;

        var more = Recognition.GetItems(user.ID, first.RecognitionId!, first.NextOffset, 5)!;

        more.Items.Count.ShouldBe(2);
        more.HasMore.ShouldBeFalse();
        more.NextOffset.ShouldBe(12);
        first.Items.Concat(more.Items).Select(i => i.Media.ExternalApiID).ShouldBeUnique();
        Provider.Calls(tag).ShouldBe(1);
        (await UsageCountAsync(user)).ShouldBe(1);
        // The same position again gives the same items (a repeated "Show more" can't skip any)
        Recognition.GetItems(user.ID, first.RecognitionId!, first.NextOffset, 5)!.Items.ShouldBe(more.Items);
    }

    [Fact]
    public async Task Recognition_BelongsToItsUser()
    {
        var user = await PremiumUserAsync();
        var other = await PremiumUserAsync();
        var first = (await Recognition.RecognizeAsync(user.ID, TestImages.Jpeg(Tag()))).Response!;

        Recognition.GetItems(other.ID, first.RecognitionId!, 10, 5).ShouldBeNull();
        Recognition.GetItems(user.ID, "no-such-recognition", 10, 5).ShouldBeNull();
    }

    [Fact]
    public async Task ShortList_HasNothingToPageThrough()
    {
        var user = await PremiumUserAsync();
        var tag = Tag();
        Provider.Answer(tag, new { outcome = "recognised", summary = "We saw a poster.", works = new[] { TestRecognitionProvider.Work($"{tag} Poster Film", "direct", 0.95) } });

        var response = (await Recognition.RecognizeAsync(user.ID, TestImages.Jpeg(tag))).Response!;

        response.Items.Single().Media.Title.ShouldBe($"{tag} Poster Film");
        response.HasMore.ShouldBeFalse();
        response.RecognitionId.ShouldBeNull();
    }

    [Theory]
    [InlineData("nothing_recognised")]
    [InlineData("not_media_related")]
    public async Task NothingToSuggest_IsAResultNotAnError_AndCounts(string outcome)
    {
        var user = await PremiumUserAsync();
        var tag = Tag();
        Provider.Answer(tag, new { outcome, summary = "We saw a mug." });

        var result = await Recognition.RecognizeAsync(user.ID, TestImages.Jpeg(tag));

        result.Status.ShouldBe(RecognitionStatus.Ok);
        result.Response!.Outcome.ShouldBe(outcome);
        result.Response.Summary.ShouldBe("We saw a mug.");
        result.Response.Items.ShouldBeEmpty();
        result.Response.RecognitionId.ShouldBeNull();
        (await UsageCountAsync(user)).ShouldBe(1);
    }

    [Fact]
    public async Task UnusableAnswer_IsNothingRecognised_AndCounts()
    {
        var user = await PremiumUserAsync();
        var tag = Tag();
        Provider.AnswerRaw(tag, null);

        var response = (await Recognition.RecognizeAsync(user.ID, TestImages.Jpeg(tag))).Response!;

        response.Outcome.ShouldBe("nothing_recognised");
        (await UsageCountAsync(user)).ShouldBe(1);
    }

    [Fact]
    public async Task Person_IsNeverNamed()
    {
        var user = await PremiumUserAsync();
        var tag = Tag();
        Provider.Answer(tag, new
        {
            outcome = "person_not_identified",
            summary = "We saw a person. People are not identified from their faces.",
            people = new[] { new { name = $"{tag} Celebrity", role = "actor", confidence = 0.99 } }
        });

        var response = (await Recognition.RecognizeAsync(user.ID, TestImages.Jpeg(tag))).Response!;

        response.Outcome.ShouldBe("person_not_identified");
        response.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task HallucinatedTitles_AreDropped()
    {
        var user = await PremiumUserAsync();
        var tag = Tag();
        var real = $"{tag} Real";
        var invented = $"{tag} Invented";
        App.Tmdb.ScriptTitle(invented);
        Provider.Answer(tag, new { outcome = "recognised", works = new[] { TestRecognitionProvider.Work(real, "direct", 0.9), TestRecognitionProvider.Work(invented, "direct", 0.9) } });

        var response = (await Recognition.RecognizeAsync(user.ID, TestImages.Jpeg(tag))).Response!;

        response.Items.Select(i => i.Media.Title).ShouldBe([real]);
    }

    [Fact]
    public async Task ProviderFailure_IsNotCounted()
    {
        var user = await PremiumUserAsync();
        var tag = Tag();
        Provider.Fail(tag, RecognitionProviderError.QuotaExceeded);

        var result = await Recognition.RecognizeAsync(user.ID, TestImages.Jpeg(tag));

        result.Status.ShouldBe(RecognitionStatus.ProviderUnavailable);
        result.Error.ShouldNotBeNullOrEmpty();
        (await UsageCountAsync(user)).ShouldBe(0);
    }

    [Fact]
    public async Task MediaSourcesDown_RefundsTheRecognition()
    {
        var user = await PremiumUserAsync();
        var tag = Tag();
        var title = $"{tag} Film";
        App.Tmdb.FailTitle(title);
        Provider.Answer(tag, new { outcome = "recognised", works = new[] { TestRecognitionProvider.Work(title, "direct", 0.9) } });

        var result = await Recognition.RecognizeAsync(user.ID, TestImages.Jpeg(tag));

        result.Status.ShouldBe(RecognitionStatus.SourceUnavailable);
        (await UsageCountAsync(user)).ShouldBe(0);
    }

    [Fact]
    public async Task OneSourceDown_GivesAPartialResult()
    {
        var user = await PremiumUserAsync();
        var tag = Tag();
        var song = $"{tag} Song";
        App.Spotify.FailTitle(song);
        Provider.Answer(tag, new
        {
            outcome = "recognised",
            works = new[] { TestRecognitionProvider.Work($"{tag} Film", "direct", 0.9), TestRecognitionProvider.Work(song, "universe", 0.8, "music", creator: "Band") }
        });

        var response = (await Recognition.RecognizeAsync(user.ID, TestImages.Jpeg(tag))).Response!;

        response.Items.Single().Media.Title.ShouldBe($"{tag} Film");
        response.Incomplete.ShouldBeTrue();
        (await UsageCountAsync(user)).ShouldBe(1);
    }

    [Fact]
    public async Task DailyLimit_StopsBeforeTheAiCall()
    {
        var user = await PremiumUserAsync();
        await Data.RecognitionUsageAsync(user, 20);
        var tag = Tag();

        var result = await Recognition.RecognizeAsync(user.ID, TestImages.Jpeg(tag));

        result.Status.ShouldBe(RecognitionStatus.DailyLimit);
        result.ResetsAt.ShouldBe(DateTime.UtcNow.Date.AddDays(1));
        Provider.Calls(tag).ShouldBe(0);
        (await UsageCountAsync(user)).ShouldBe(20);
    }

    [Fact]
    public async Task YesterdaysRecognitions_DontCount()
    {
        var user = await PremiumUserAsync();
        await Data.RecognitionUsageAsync(user, 20, DateTime.UtcNow.Date.AddMinutes(-1));
        await Data.RecognitionUsageAsync(user, 3);

        var allowance = await Recognition.GetAllowanceAsync(user.ID);

        allowance.ShouldSatisfyAllConditions(a => a.Enabled.ShouldBeTrue(), a => a.Premium.ShouldBeTrue(), a => a.Remaining.ShouldBe(17));
        (await Recognition.RecognizeAsync(user.ID, TestImages.Jpeg(Tag()))).Status.ShouldBe(RecognitionStatus.Ok);
    }

    [Fact]
    public async Task FreeUser_SeesTheFeatureWithoutCounters()
    {
        var user = await Data.UserAsync();

        var allowance = await Recognition.GetAllowanceAsync(user.ID);

        allowance.ShouldBe(new ReviewApp.Api.DTOs.RecognitionAllowanceDto { Enabled = true, Premium = false });
    }

    [Fact]
    public async Task SecondRecognitionWhileOneRuns_IsRefused_AndNotCounted()
    {
        var user = await PremiumUserAsync();
        var tag = Tag();
        var release = new TaskCompletionSource();
        Provider.Hold(tag, release.Task);

        var running = Recognition.RecognizeAsync(user.ID, TestImages.Jpeg(tag));
        await TestWait.UntilAsync(() => Provider.Calls(tag) == 1);

        var second = await Recognition.RecognizeAsync(user.ID, TestImages.Jpeg(Tag()));
        release.SetResult();

        second.Status.ShouldBe(RecognitionStatus.Busy);
        (await running).Status.ShouldBe(RecognitionStatus.Ok);
        (await UsageCountAsync(user)).ShouldBe(1);
        // Free again afterwards
        (await Recognition.RecognizeAsync(user.ID, TestImages.Jpeg(Tag()))).Status.ShouldBe(RecognitionStatus.Ok);
    }

    [Fact]
    public async Task BadImages_AreRefusedBeforeTheAiCall()
    {
        var user = await PremiumUserAsync();
        var tag = Tag();

        (await Recognition.RecognizeAsync(user.ID, TestImages.Heic())).Status.ShouldBe(RecognitionStatus.BadImage);
        (await Recognition.RecognizeAsync(user.ID, [])).Status.ShouldBe(RecognitionStatus.BadImage);
        (await Recognition.RecognizeAsync(user.ID, TestImages.Jpeg(tag, 20000, 20000))).Status.ShouldBe(RecognitionStatus.TooLarge);

        Provider.Calls(tag).ShouldBe(0);
        (await UsageCountAsync(user)).ShouldBe(0);
    }
}
