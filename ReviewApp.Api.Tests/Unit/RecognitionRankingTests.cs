using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services.Recognition;

namespace ReviewApp.Api.Tests.Unit;

public class RecognitionRankingTests
{
    private static readonly RecognitionOptions Options = new() { MinConfidence = 0.4, SimilarFillThreshold = 3, TotalCap = 60 };

    private static RankCandidate Candidate(string id, RecognitionTier tier = RecognitionTier.Direct, double confidence = 0.9, double popularity = 10,
        string? title = null, string? releaseDate = null, MediaType type = MediaType.Movie, string? creator = null) =>
        new(new MediaDto { ID = id + type, ExternalApiID = id, MediaType = type, Title = title ?? $"Title {id}", ReleaseDate = releaseDate, Creator = creator },
            tier, confidence, popularity);

    private static List<string> Ids(IEnumerable<RankedItem> items) => items.Select(i => i.Media.ExternalApiID).ToList();

    [Fact]
    public void Tiers_ComeFirst_WhateverTheConfidence()
    {
        var ranked = RecognitionRanking.Rank(
        [
            Candidate("people", RecognitionTier.People, 1.0),
            Candidate("universe", RecognitionTier.Universe, 0.95),
            Candidate("direct", RecognitionTier.Direct, 0.5)
        ], Options);

        Ids(ranked).ShouldBe(["direct", "universe", "people"]);
        ranked.Select(r => r.Tier).ShouldBe([RecognitionTier.Direct, RecognitionTier.Universe, RecognitionTier.People]);
    }

    [Fact]
    public void WithinATier_ConfidenceThenPopularity_CloseConfidencesCountAsEqual()
    {
        var ranked = RecognitionRanking.Rank(
        [
            Candidate("sure-unpopular", confidence: 0.97, popularity: 1),
            Candidate("unsure", confidence: 0.6, popularity: 1000),
            // 0.96 and 0.97 are in the same bucket, so popularity decides
            Candidate("sure-popular", confidence: 0.96, popularity: 50)
        ], Options);

        Ids(ranked).ShouldBe(["sure-popular", "sure-unpopular", "unsure"]);
    }

    [Fact]
    public void Ties_AreBrokenByReleaseDateThenTitle_UnknownDatesLast()
    {
        var ranked = RecognitionRanking.Rank(
        [
            Candidate("undated", title: "A"),
            Candidate("third", title: "Part 3", releaseDate: "2013-04-24"),
            Candidate("first", title: "Part 1", releaseDate: "2008-04-30"),
            Candidate("b", title: "Same day B", releaseDate: "2010-01-01"),
            Candidate("a", title: "Same day A", releaseDate: "2010-01-01")
        ], Options);

        Ids(ranked).ShouldBe(["first", "a", "b", "third", "undated"]);
    }

    [Fact]
    public void SameItemThroughSeveralRelations_KeepsTheClosest()
    {
        var ranked = RecognitionRanking.Rank(
        [
            Candidate("1", RecognitionTier.People, 0.99),
            Candidate("1", RecognitionTier.Direct, 0.7),
            Candidate("1", RecognitionTier.Universe, 0.9)
        ], Options);

        ranked.Single().Tier.ShouldBe(RecognitionTier.Direct);
    }

    [Fact]
    public void SameIdOfAnotherType_IsADifferentItem()
    {
        var ranked = RecognitionRanking.Rank([Candidate("1", type: MediaType.Movie), Candidate("1", title: "Other", type: MediaType.Series)], Options);

        ranked.Count.ShouldBe(2);
    }

    [Fact]
    public void ObviousDuplicates_SameTitleYearAndArtist_AreCollapsed()
    {
        var ranked = RecognitionRanking.Rank(
        [
            Candidate("orig", title: "Back in Black", releaseDate: "1980-07-25", type: MediaType.Music, creator: "AC/DC", popularity: 80),
            Candidate("remaster", title: "Back in Black", releaseDate: "1980", type: MediaType.Music, creator: "AC/DC", popularity: 20),
            // Same title by someone else is a different song
            Candidate("cover", title: "Back in Black", releaseDate: "1980", type: MediaType.Music, creator: "Cover Band"),
            // A remake is a different film
            Candidate("remake", title: "Title orig", releaseDate: "2020-01-01"),
            Candidate("orig-film", title: "Title orig", releaseDate: "1990-01-01")
        ], Options);

        Ids(ranked).ShouldBe(["orig", "cover", "orig-film", "remake"], ignoreOrder: true);
    }

    [Fact]
    public void LowConfidence_IsDropped()
    {
        var ranked = RecognitionRanking.Rank([Candidate("sure", confidence: 0.4), Candidate("guess", confidence: 0.39)], Options);

        Ids(ranked).ShouldBe(["sure"]);
    }

    [Fact]
    public void Similar_OnlyFillsAThinList()
    {
        var thin = RecognitionRanking.Rank([Candidate("1"), Candidate("2"), Candidate("s", RecognitionTier.Similar)], Options);
        var full = RecognitionRanking.Rank([Candidate("1"), Candidate("2"), Candidate("3"), Candidate("s", RecognitionTier.Similar)], Options);

        Ids(thin).ShouldContain("s");
        Ids(full).ShouldNotContain("s");
    }

    [Fact]
    public void List_IsCapped_AtTheClosestItems()
    {
        var candidates = Enumerable.Range(0, 80)
            .Select(i => Candidate($"{i}", i < 40 ? RecognitionTier.Direct : RecognitionTier.People, title: $"T{i:00}"))
            .ToList();

        var ranked = RecognitionRanking.Rank(candidates, new RecognitionOptions { MinConfidence = 0.4, SimilarFillThreshold = 3, TotalCap = 50 });

        ranked.Count.ShouldBe(50);
        ranked.Count(r => r.Tier == RecognitionTier.Direct).ShouldBe(40);
    }

    [Fact]
    public void SameInput_AlwaysGivesTheSameOrder()
    {
        var candidates = Enumerable.Range(0, 30)
            .Select(i => Candidate($"{i}", (RecognitionTier)(i % 3), 0.9, popularity: i % 4, title: "Same"))
            .ToList();

        var first = Ids(RecognitionRanking.Rank(candidates, Options));
        var shuffled = Ids(RecognitionRanking.Rank(candidates.OrderBy(_ => Random.Shared.Next()).ToList(), Options));

        shuffled.ShouldBe(first);
    }

    [Fact]
    public void Paging_FirstTenThenFives_NeverRepeatsOrSkips()
    {
        var ranked = RecognitionRanking.Rank(Enumerable.Range(0, 23).Select(i => Candidate($"{i:00}", title: $"T{i:00}")).ToList(), Options);

        var first = RecognitionRanking.Page(ranked, 0, 10);
        first.Items.Count.ShouldBe(10);
        first.HasMore.ShouldBeTrue();
        first.Total.ShouldBe(23);

        var seen = Ids(first.Items);
        var next = first.NextOffset;
        var pages = 0;
        while (true)
        {
            var page = RecognitionRanking.Page(ranked, next, 5);
            seen.AddRange(Ids(page.Items));
            next = page.NextOffset;
            pages++;
            if (!page.HasMore) break;
        }

        pages.ShouldBe(3); // 5 + 5 + 3
        seen.ShouldBe(Ids(ranked));
    }

    [Theory]
    [InlineData(-5, 5, 5, 5)]
    [InlineData(20, 5, 20, 0)]
    [InlineData(100, 5, 20, 0)]
    [InlineData(18, 5, 20, 2)]
    [InlineData(0, -1, 0, 0)]
    public void Paging_OutOfRangeValues_AreClamped(int offset, int count, int expectedNext, int expectedItems)
    {
        var ranked = RecognitionRanking.Rank(Enumerable.Range(0, 20).Select(i => Candidate($"{i:00}", title: $"T{i:00}")).ToList(), Options);

        var page = RecognitionRanking.Page(ranked, offset, count);

        page.Items.Count.ShouldBe(expectedItems);
        page.NextOffset.ShouldBe(expectedNext);
    }
}
