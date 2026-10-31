using System.Collections.Concurrent;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Services;
using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Tests.Support;

// TMDb stand-in: every numeric ID exists ("Movie 42" / "Series 42") unless it was marked unknown,
// which then answers like the real service does on a failed request (an empty item).
public class FakeTmdbService : ITmdbService
{
    private readonly ConcurrentDictionary<string, byte> _unknown = new();
    private int _lookups;

    public int Lookups => _lookups;

    public void MarkUnknown(string id) => _unknown[id] = 0;

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

    public void MarkUnknown(string id) => _unknown[id] = 0;

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
