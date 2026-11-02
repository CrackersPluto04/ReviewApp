using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services;
using ReviewApp.Api.Services.Recognition;
using ReviewApp.Api.Tests.Support;

namespace ReviewApp.Api.Tests.Unit;

// Resolution of the AI's answer through (fake) TMDb / Spotify: matching, expansion, and the limits on lookups
public class MediaResolverTests
{
    private readonly FakeTmdbService _tmdb = new();
    private readonly FakeSpotifyService _spotify = new();

    private MediaResolver Resolver(RecognitionOptions? options = null) =>
        // ToMediaDto never touches the database
        new(_tmdb, _spotify, new MediaService(null!, _tmdb, _spotify), new MemoryCache(new MemoryCacheOptions()),
            Options.Create(options ?? new RecognitionOptions()), NullLogger<MediaResolver>.Instance);

    private static RecognitionAnswer Answer(IEnumerable<RecognitionWork> works, IEnumerable<RecognitionPerson>? people = null) =>
        new(RecognitionOutcome.Recognised, "We saw it.", null, [], (people ?? []).ToList(), works.ToList());

    private static RecognitionWork Movie(string title, int? year = null, RecognitionTier relation = RecognitionTier.Direct, double confidence = 0.9) =>
        new(title, MediaType.Movie, year, null, relation, confidence);

    private static TmdbItemDto Hit(int id, string title, string? date = null, double popularity = 10) =>
        new() { ID = id, Title = title, ReleaseDate = date, Popularity = popularity, VoteCount = 100 };

    [Fact]
    public async Task ExactTitleAndFittingYear_AreMatched_OthersDropped()
    {
        var title = TestData.Unique("Iron Man ");
        _tmdb.ScriptTitle(title,
            Hit(1, title + " 2", "2008-05-02", popularity: 99),  // other title
            Hit(2, title, "1994-01-01", popularity: 80),          // too old
            Hit(3, title, "2008-04-30", popularity: 50),
            Hit(4, title, "2009-01-01", popularity: 70));         // a year off, but the exact year wins

        var result = await Resolver().ResolveAsync(Answer([Movie(title, 2008)]), CancellationToken.None);

        var candidate = result.Candidates.Single();
        candidate.Media.ExternalApiID.ShouldBe("3");
        candidate.Media.MediaType.ShouldBe(MediaType.Movie);
        candidate.Tier.ShouldBe(RecognitionTier.Direct);
        candidate.Confidence.ShouldBe(0.9);
        candidate.Popularity.ShouldBe(50);
    }

    [Fact]
    public async Task UnknownTitle_IsDropped_WithoutCountingAsAFailure()
    {
        var title = TestData.Unique("Made Up ");
        _tmdb.ScriptTitle(title);

        var result = await Resolver().ResolveAsync(Answer([Movie(title)]), CancellationToken.None);

        result.Candidates.ShouldBeEmpty();
        result.Failures.ShouldBe(0);
    }

    [Fact]
    public async Task Series_AreMatchedByName()
    {
        var title = TestData.Unique("Show ");
        _tmdb.ScriptTitle(title, new TmdbItemDto { ID = 9, Name = title, FirstAirDate = "2021-01-15" });

        var result = await Resolver().ResolveAsync(Answer([new RecognitionWork(title, MediaType.Series, 2021, null, RecognitionTier.Universe, 0.7)]),
            CancellationToken.None);

        result.Candidates.Single().Media.ShouldSatisfyAllConditions(
            m => m.MediaType.ShouldBe(MediaType.Series),
            m => m.ExternalApiID.ShouldBe("9"),
            m => m.ReleaseDate.ShouldBe("2021-01-15"));
    }

    [Fact]
    public async Task Albums_LandOnTheirMostPopularTrack_ByTheRightArtist()
    {
        var album = TestData.Unique("Album ");
        _spotify.ScriptTitle(album,
            Track("cover", "Song", album, "Cover Band", 90),
            Track("deep-cut", "Other Song", album, "The Band", 30),
            Track("hit", "Hit Song", album, "The Band", 70));

        var result = await Resolver().ResolveAsync(Answer([new RecognitionWork(album, MediaType.Music, null, "The Band", RecognitionTier.Direct, 0.9)]),
            CancellationToken.None);

        result.Candidates.Single().Media.ShouldSatisfyAllConditions(
            m => m.ExternalApiID.ShouldBe("hit"),
            m => m.MediaType.ShouldBe(MediaType.Music),
            m => m.Creator.ShouldBe("The Band"));
    }

    private static SpotifyTrackDto Track(string id, string name, string album, string artist, int popularity) => new()
    {
        Id = id,
        Name = name,
        Popularity = popularity,
        Album = new SpotifyAlbumDto { Name = album, ReleaseDate = "1999" },
        Artists = [new SpotifyArtistDto { Name = artist }]
    };

    [Fact]
    public async Task DirectMovies_BringTheirFilmSeries()
    {
        var title = TestData.Unique("Saga ");
        _tmdb.ScriptTitle(title, Hit(100, title, "2001-01-01"));
        _tmdb.ScriptCollection(100, 555,
            Hit(100, title, "2001-01-01"),
            Hit(101, title + " II", "2004-01-01", popularity: 30),
            Hit(102, title + " III (announced)"));

        var result = await Resolver().ResolveAsync(Answer([Movie(title, confidence: 1.0)]), CancellationToken.None);

        var sequel = result.Candidates.Single(c => c.Media.ExternalApiID == "101");
        sequel.Tier.ShouldBe(RecognitionTier.Direct);
        sequel.Confidence.ShouldBeLessThan(1.0);
        result.Candidates.ShouldNotContain(c => c.Media.ExternalApiID == "102");
    }

    [Fact]
    public async Task ConnectedActors_BringTheirBestKnownWork_WithoutAppearances()
    {
        var title = TestData.Unique("Hero ");
        var actor = TestData.Unique("Actor ");
        _tmdb.ScriptPerson(actor, 77, new TmdbPersonCreditsDto
        {
            Cast =
            [
                Credit(1, "movie", "Big Film", popularity: 90),
                Credit(2, "tv", "Drama Show", popularity: 80),
                Credit(3, "tv", "Late Night Talk", popularity: 99, genres: [10767]),
                Credit(4, "movie", "Obscure Short", popularity: 85, votes: 3),
                Credit(5, "tv", "Guest Spot", popularity: 95, episodes: 2)
            ]
        });

        var result = await Resolver(new RecognitionOptions { ExpandCollections = false })
            .ResolveAsync(Answer([Movie(title)], [new RecognitionPerson(actor, "actor", 0.9)]), CancellationToken.None);

        var people = result.Candidates.Where(c => c.Tier == RecognitionTier.People).ToList();
        people.Select(c => c.Media.Title).ShouldBe(["Big Film", "Drama Show"], ignoreOrder: true);
        people.Single(c => c.Media.Title == "Drama Show").Media.MediaType.ShouldBe(MediaType.Series);
    }

    [Fact]
    public async Task UnsureOrOtherPeople_AreNotExpanded()
    {
        var unsure = TestData.Unique("Unsure ");
        var musician = TestData.Unique("Musician ");
        _tmdb.ScriptPerson(unsure, 1, new TmdbPersonCreditsDto { Cast = [Credit(1, "movie", "A")] });
        _tmdb.ScriptPerson(musician, 2, new TmdbPersonCreditsDto { Cast = [Credit(2, "movie", "B")] });

        var result = await Resolver().ResolveAsync(Answer([], [new RecognitionPerson(unsure, "actor", 0.3), new RecognitionPerson(musician, "musician", 0.9)]),
            CancellationToken.None);

        result.Candidates.ShouldBeEmpty();
        result.Lookups.ShouldBe(0);
    }

    private static TmdbCreditDto Credit(int id, string type, string title, double popularity = 10, int votes = 100, List<int>? genres = null,
        int? episodes = null) => new()
    {
        ID = id,
        EpisodeCount = episodes ?? (type == "tv" ? 20 : null),
        MediaType = type,
        Title = type == "movie" ? title : null,
        Name = type == "tv" ? title : null,
        Popularity = popularity,
        VoteCount = votes,
        GenreIds = genres ?? []
    };

    [Fact]
    public async Task Outage_IsReportedAsFailures_AndTheRestStillResolves()
    {
        var down = TestData.Unique("Down ");
        var up = TestData.Unique("Up ");
        _tmdb.FailTitle(down);

        var result = await Resolver().ResolveAsync(Answer([Movie(down), Movie(up)]), CancellationToken.None);

        result.Failures.ShouldBe(1);
        result.Candidates.Single().Media.Title.ShouldBe(up);
    }

    [Fact]
    public async Task RepeatedFailures_StopTheLookups()
    {
        var titles = Enumerable.Range(0, 10).Select(_ => TestData.Unique("Down ")).ToList();
        titles.ForEach(_tmdb.FailTitle);
        var before = _tmdb.RecognitionLookups;

        var result = await Resolver(new RecognitionOptions { MaxParallelLookups = 1, ExpandCollections = false })
            .ResolveAsync(Answer(titles.Select(t => Movie(t))), CancellationToken.None);

        (_tmdb.RecognitionLookups - before).ShouldBe(3);
        result.Failures.ShouldBe(10);
    }

    [Fact]
    public async Task Lookups_StayWithinTheBudget()
    {
        var titles = Enumerable.Range(0, 12).Select(_ => TestData.Unique("Film ")).ToList();
        var before = _tmdb.RecognitionLookups;

        var result = await Resolver(new RecognitionOptions { MaxLookups = 5, ExpandCollections = false })
            .ResolveAsync(Answer(titles.Select(t => Movie(t))), CancellationToken.None);

        (_tmdb.RecognitionLookups - before).ShouldBe(5);
        result.Candidates.Count.ShouldBe(5);
        result.Failures.ShouldBe(0);
    }

    [Fact]
    public async Task RepeatedTitles_AreAnsweredFromTheCache()
    {
        var title = TestData.Unique("Cached ");
        var resolver = Resolver(new RecognitionOptions { ExpandCollections = false });
        var before = _tmdb.RecognitionLookups;

        await resolver.ResolveAsync(Answer([Movie(title)]), CancellationToken.None);
        var second = await resolver.ResolveAsync(Answer([Movie(title)]), CancellationToken.None);

        (_tmdb.RecognitionLookups - before).ShouldBe(1);
        second.Candidates.Single().Media.Title.ShouldBe(title);
    }
}
