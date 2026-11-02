using System.Collections.Concurrent;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Services;
using ReviewApp.Api.Services.Interfaces;
using ReviewApp.Api.Services.Recognition;

namespace ReviewApp.Api.Tests.Support;

// TMDb stand-in: every numeric ID exists ("Movie 42" / "Series 42") unless it was marked unknown,
// which then answers like the real service does on a failed request (an empty item).
// The recognition lookups (Find*) find every title by default: one exact hit with a stable ID derived from the title.
// Tests script other answers per (unique) title, so they stay isolated while sharing the fake.
public class FakeTmdbService : ITmdbService
{
    private readonly ConcurrentDictionary<string, byte> _unknown = new();
    private readonly ConcurrentDictionary<string, List<TmdbItemDto>> _titleHits = new();
    private readonly ConcurrentDictionary<string, byte> _failingTitles = new();
    private readonly ConcurrentDictionary<int, int> _movieCollections = new();
    private readonly ConcurrentDictionary<int, List<TmdbItemDto>> _collections = new();
    private readonly ConcurrentDictionary<string, TmdbPersonDto> _people = new();
    private readonly ConcurrentDictionary<int, TmdbPersonCreditsDto> _credits = new();
    private int _lookups;
    private int _recognitionLookups;

    public int Lookups => _lookups;
    // Calls of the Find* / collection / person methods
    public int RecognitionLookups => _recognitionLookups;

    public void MarkUnknown(string id) => _unknown[id] = 0;

    // The hits a movie or series title search returns (empty = not found)
    public void ScriptTitle(string title, params TmdbItemDto[] hits) => _titleHits[title] = [.. hits];
    // Searching this title fails like an outage
    public void FailTitle(string title) => _failingTitles[title] = 0;
    public void ScriptCollection(int movieId, int collectionId, params TmdbItemDto[] parts)
    {
        _movieCollections[movieId] = collectionId;
        _collections[collectionId] = [.. parts];
    }
    public void ScriptPerson(string name, int personId, TmdbPersonCreditsDto credits)
    {
        _people[name] = new TmdbPersonDto { ID = personId, Name = name, Popularity = 50 };
        _credits[personId] = credits;
    }

    // The ID the default hit of a title gets
    public static int IdFor(string title) => (int)(Fnv(title) % 900_000_000) + 1_000_000;

    public Task<List<TmdbItemDto>> FindMoviesAsync(string title, CancellationToken cancellationToken) =>
        Task.FromResult(Find(title, movie: true));

    public Task<List<TmdbItemDto>> FindSeriesAsync(string title, CancellationToken cancellationToken) =>
        Task.FromResult(Find(title, movie: false));

    public Task<int?> GetMovieCollectionIdAsync(int movieId, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _recognitionLookups);
        return Task.FromResult(_movieCollections.TryGetValue(movieId, out var id) ? id : (int?)null);
    }

    public Task<List<TmdbItemDto>> GetCollectionPartsAsync(int collectionId, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _recognitionLookups);
        return Task.FromResult(_collections.TryGetValue(collectionId, out var parts) ? parts : []);
    }

    public Task<List<TmdbPersonDto>> FindPeopleAsync(string name, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _recognitionLookups);
        return Task.FromResult(_people.TryGetValue(name, out var person) ? new List<TmdbPersonDto> { person } : []);
    }

    public Task<TmdbPersonCreditsDto> GetPersonCreditsAsync(int personId, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _recognitionLookups);
        return Task.FromResult(_credits.TryGetValue(personId, out var credits) ? credits : new TmdbPersonCreditsDto());
    }

    private List<TmdbItemDto> Find(string title, bool movie)
    {
        Interlocked.Increment(ref _recognitionLookups);
        if (_failingTitles.ContainsKey(title))
            throw new MediaSourceUnavailableException("TMDb");
        if (_titleHits.TryGetValue(title, out var hits))
            return hits;

        return [new TmdbItemDto { ID = IdFor(title), Title = movie ? title : null, Name = movie ? null : title, Popularity = 10, VoteCount = 100 }];
    }

    private static uint Fnv(string text)
    {
        var hash = 2166136261;
        foreach (var c in text)
            hash = (hash ^ c) * 16777619;
        return hash;
    }

    public Task<TmdbSearchResponseDto> SearchMoviesAsync(string query, int page = 1) => Task.FromResult(Results("Movie", query));
    public Task<TmdbSearchResponseDto> SearchSeriesAsync(string query, int page = 1) => Task.FromResult(Results("Series", query));
    public Task<TmdbSearchResponseDto> DiscoverMoviesAsync(TmdbParams p) => Task.FromResult(Results("Movie", "discover"));
    public Task<TmdbSearchResponseDto> DiscoverSeriesAsync(TmdbParams p) => Task.FromResult(Results("Series", "discover"));
    public Task<TmdbItemDto> GetMovieByIdAsync(string id) => Task.FromResult(Item("Movie", id));
    public Task<TmdbItemDto> GetSeriesByIdAsync(string id) => Task.FromResult(Item("Series", id));

    private TmdbItemDto Item(string kind, string id)
    {
        Interlocked.Increment(ref _lookups);
        if (_unknown.ContainsKey(id) || !int.TryParse(id, out var numericId))
            return new TmdbItemDto();

        return new TmdbItemDto
        {
            ID = numericId,
            Title = kind == "Movie" ? $"{kind} {id}" : null,
            Name = kind == "Series" ? $"{kind} {id}" : null,
            ReleaseDate = kind == "Movie" ? "2020-05-17" : null,
            FirstAirDate = kind == "Series" ? "2019-01-02" : null,
            PosterPath = $"/poster{id}.jpg",
            Overview = $"Overview of {kind} {id}"
        };
    }

    private static TmdbSearchResponseDto Results(string kind, string query) => new()
    {
        Results = [new TmdbItemDto { ID = 1, Title = $"{kind} {query}", Name = $"{kind} {query}", ReleaseDate = "2021-01-01" }],
        TotalCount = 1
    };
}

// Spotify stand-in, same idea as FakeTmdbService
public class FakeSpotifyService : ISpotifyService
{
    private readonly ConcurrentDictionary<string, byte> _unknown = new();
    private readonly ConcurrentDictionary<string, List<SpotifyTrackDto>> _titleHits = new();
    private readonly ConcurrentDictionary<string, byte> _failingTitles = new();

    public void MarkUnknown(string id) => _unknown[id] = 0;

    public void ScriptTitle(string title, params SpotifyTrackDto[] hits) => _titleHits[title] = [.. hits];
    public void FailTitle(string title) => _failingTitles[title] = 0;

    // By default a track named like the title, by the artist asked for
    public Task<List<SpotifyTrackDto>> FindTracksAsync(string title, string? artist, CancellationToken cancellationToken)
    {
        if (_failingTitles.ContainsKey(title))
            throw new MediaSourceUnavailableException("Spotify");
        if (_titleHits.TryGetValue(title, out var hits))
            return Task.FromResult(hits);

        var id = $"sp{FakeTmdbService.IdFor(title)}";
        return Task.FromResult(new List<SpotifyTrackDto>
        {
            new()
            {
                Id = id,
                Name = title,
                Popularity = 40,
                Album = new SpotifyAlbumDto { Name = title, ReleaseDate = "2001-01-01", Images = [new SpotifyImageDto { Url = $"https://img.test/{id}.jpg" }] },
                Artists = [new SpotifyArtistDto { Name = artist ?? "Artist" }]
            }
        });
    }

    public Task<SpotifyTracksDto> SearchMusicAsync(string query, int page = 1) => Task.FromResult(Tracks(query));
    public Task<SpotifyTracksDto> DiscoverMusicAsync(SpotifyParams p) => Task.FromResult(Tracks("discover"));

    public Task<SpotifyTrackDto> GetMusicByIdAsync(string id) =>
        Task.FromResult(_unknown.ContainsKey(id) ? new SpotifyTrackDto() : Track(id));

    private static SpotifyTrackDto Track(string id) => new()
    {
        Id = id,
        Name = $"Track {id}",
        Album = new SpotifyAlbumDto { ReleaseDate = "2018-03-04", Images = [new SpotifyImageDto { Url = $"https://img.test/{id}.jpg" }] },
        Artists = [new SpotifyArtistDto { Name = "Artist A" }, new SpotifyArtistDto { Name = "Artist B" }]
    };

    private static SpotifyTracksDto Tracks(string query) => new() { Items = [Track(query)], TotalCount = 1 };
}
