using System.Net;
using Microsoft.Extensions.Configuration;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Services;
using ReviewApp.Api.Services.Recognition;
using ReviewApp.Api.Tests.Support;

namespace ReviewApp.Api.Tests.Unit;

// The typed TMDb / Spotify clients against a stubbed HTTP layer: the requests they build and how they read answers
public class ExternalApiClientTests
{
    private static readonly IConfiguration Config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tmdb:BaseUrl"] = "https://tmdb.test/3/",
            ["Tmdb:ApiKey"] = "tmdb-key",
            ["Spotify:ClientId"] = "client",
            ["Spotify:ClientSecret"] = "secret"
        })
        .Build();

    private const string SpotifyToken = """{ "access_token": "spotify-token" }""";

    [Fact]
    public async Task Tmdb_SearchEscapesQueryAndAddsApiKey()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{ "results": [ { "id": 1, "title": "A" } ], "total_results": 1 }"""));
        var service = new TmdbService(new HttpClient(handler), Config);

        var result = await service.SearchMoviesAsync("star wars & co", 2);

        result.TotalCount.ShouldBe(1);
        result.Results.Single().DisplayTitle.ShouldBe("A");
        handler.Requests.Single().RequestUri!.AbsoluteUri
            .ShouldBe("https://tmdb.test/3/search/movie?query=star%20wars%20%26%20co&page=2&api_key=tmdb-key");
    }

    [Fact]
    public async Task Tmdb_DiscoverBuildsFilters()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{ "results": [] }"""));
        var service = new TmdbService(new HttpClient(handler), Config);

        await service.DiscoverSeriesAsync(new TmdbParams { Page = 3, Year = 2020, WithGenres = "18,35", MinRuntime = 20, MaxRuntime = 60 });

        var uri = handler.Requests.Single().RequestUri!.AbsoluteUri;
        uri.ShouldStartWith("https://tmdb.test/3/discover/tv?page=3&sort_by=popularity.desc");
        uri.ShouldContain("first_air_date_year=2020");
        uri.ShouldContain("with_genres=18%2C35");
        uri.ShouldContain("with_runtime.gte=20");
        uri.ShouldContain("with_runtime.lte=60");
    }

    [Fact]
    public async Task Tmdb_FailedRequest_ReturnsEmptyResult()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var service = new TmdbService(new HttpClient(handler), Config);

        (await service.SearchMoviesAsync("x")).Results.ShouldBeEmpty();
        (await service.GetMovieByIdAsync("1")).ID.ShouldBe(-1);
    }

    [Fact]
    public async Task Tmdb_GetByIdEscapesTheId()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{ "id": 5, "name": "Show" }"""));
        var service = new TmdbService(new HttpClient(handler), Config);

        var item = await service.GetSeriesByIdAsync("5/../admin");

        item.DisplayTitle.ShouldBe("Show");
        handler.Requests.Single().RequestUri!.AbsolutePath.ShouldBe("/3/tv/5%2F..%2Fadmin");
    }

    [Fact]
    public async Task Spotify_GetsTokenThenCallsApiWithBearer()
    {
        var handler = new StubHttpHandler(request => request.RequestUri!.Host == "accounts.spotify.com"
            ? StubHttpHandler.Json(SpotifyToken)
            : StubHttpHandler.Json("""{ "id": "t1", "name": "Track", "artists": [ { "name": "X" } ] }"""));
        var service = new SpotifyService(new HttpClient(handler), Config);

        var track = await service.GetMusicByIdAsync("t1");

        track.Name.ShouldBe("Track");
        handler.Requests[0].Method.ShouldBe(HttpMethod.Post);
        handler.Requests[0].Headers.Authorization!.Scheme.ShouldBe("Basic");
        handler.Requests[0].Headers.Authorization!.Parameter.ShouldBe(Convert.ToBase64String("client:secret"u8.ToArray()));
        handler.Requests[1].Headers.Authorization!.ToString().ShouldBe("Bearer spotify-token");
        handler.Requests[1].RequestUri!.AbsoluteUri.ShouldBe("https://api.spotify.com/v1/tracks/t1");
    }

    [Fact]
    public async Task Spotify_SearchPagesByTen()
    {
        var handler = new StubHttpHandler(request => request.RequestUri!.Host == "accounts.spotify.com"
            ? StubHttpHandler.Json(SpotifyToken)
            : StubHttpHandler.Json("""{ "tracks": { "total": 25, "items": [ { "id": "a" } ] } }"""));
        var service = new SpotifyService(new HttpClient(handler), Config);

        var result = await service.SearchMusicAsync("lo fi", 3);

        result.TotalCount.ShouldBe(25);
        handler.Requests[1].RequestUri!.AbsoluteUri.ShouldBe("https://api.spotify.com/v1/search?q=lo%20fi&type=track&limit=10&offset=20");
    }

    [Fact]
    public async Task Spotify_DiscoverEscapesEveryUserValue()
    {
        var handler = new StubHttpHandler(request => request.RequestUri!.Host == "accounts.spotify.com"
            ? StubHttpHandler.Json(SpotifyToken)
            : StubHttpHandler.Json("""{ "tracks": { "items": [] } }"""));
        var service = new SpotifyService(new HttpClient(handler), Config);

        await service.DiscoverMusicAsync(new SpotifyParams { Genre = "Rock", Year = "1990-1999", Market = "US&limit=50" });

        var query = handler.Requests[1].RequestUri!.Query;
        query.ShouldContain("q=genre%3Arock%20year%3A1990-1999");
        // A crafted market must not add its own query parameters
        query.ShouldContain("market=US%26limit%3D50");
        query.Split('&').Count(p => p.StartsWith("limit=")).ShouldBe(1);
    }

    [Fact]
    public async Task Spotify_FailedTokenRequest_Throws()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var service = new SpotifyService(new HttpClient(handler), Config);

        await Should.ThrowAsync<HttpRequestException>(() => service.GetMusicByIdAsync("t1"));
    }

    /* Camera recognition lookups: strict, an outage is not "not found" */

    [Fact]
    public async Task Tmdb_FindExcludesAdultTitles()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""
            { "results": [ { "id": 1, "title": "A", "popularity": 12.5, "vote_count": 300 }, { "id": 2, "title": "A", "adult": true } ] }
            """));
        var service = new TmdbService(new HttpClient(handler), Config);

        var hits = await service.FindMoviesAsync("A & B", CancellationToken.None);

        hits.Single().ShouldSatisfyAllConditions(h => h.ID.ShouldBe(1), h => h.Popularity.ShouldBe(12.5), h => h.VoteCount.ShouldBe(300));
        handler.Requests.Single().RequestUri!.AbsoluteUri
            .ShouldBe("https://tmdb.test/3/search/movie?query=A%20%26%20B&include_adult=false&api_key=tmdb-key");
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task Tmdb_FindThrowsOnFailure(HttpStatusCode status)
    {
        var service = new TmdbService(new HttpClient(new StubHttpHandler(_ => new HttpResponseMessage(status))), Config);

        await Should.ThrowAsync<MediaSourceUnavailableException>(() => service.FindSeriesAsync("x", CancellationToken.None));
    }

    [Fact]
    public async Task Tmdb_FindThrowsOnNetworkFailureAndGarbage()
    {
        var offline = new TmdbService(new HttpClient(new StubHttpHandler(_ => throw new HttpRequestException())), Config);
        var garbage = new TmdbService(new HttpClient(new StubHttpHandler(_ => StubHttpHandler.Json("<html>"))), Config);

        await Should.ThrowAsync<MediaSourceUnavailableException>(() => offline.FindMoviesAsync("x", CancellationToken.None));
        await Should.ThrowAsync<MediaSourceUnavailableException>(() => garbage.FindMoviesAsync("x", CancellationToken.None));
    }

    [Fact]
    public async Task Tmdb_NotFound_IsAnEmptyAnswer()
    {
        var service = new TmdbService(new HttpClient(new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))), Config);

        (await service.GetCollectionPartsAsync(5, CancellationToken.None)).ShouldBeEmpty();
        (await service.GetMovieCollectionIdAsync(5, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task Tmdb_CollectionAndPersonCredits()
    {
        var handler = new StubHttpHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/3/movie/1726" => StubHttpHandler.Json("""{ "id": 1726, "title": "Iron Man", "belongs_to_collection": { "id": 131292 } }"""),
            "/3/collection/131292" => StubHttpHandler.Json("""{ "parts": [ { "id": 1726, "title": "Iron Man" }, { "id": 2, "title": "X", "adult": true } ] }"""),
            "/3/person/3223/combined_credits" => StubHttpHandler.Json("""
                { "cast": [ { "id": 1, "media_type": "movie", "title": "A" }, { "id": 9, "media_type": "movie", "adult": true } ],
                  "crew": [ { "id": 2, "media_type": "tv", "name": "B", "job": "Director", "genre_ids": [18] } ] }
                """),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var service = new TmdbService(new HttpClient(handler), Config);

        (await service.GetMovieCollectionIdAsync(1726, CancellationToken.None)).ShouldBe(131292);
        (await service.GetCollectionPartsAsync(131292, CancellationToken.None)).Single().ID.ShouldBe(1726);
        var credits = await service.GetPersonCreditsAsync(3223, CancellationToken.None);
        credits.Cast.Single().MediaType.ShouldBe("movie");
        credits.Crew.Single().ShouldSatisfyAllConditions(c => c.Job.ShouldBe("Director"), c => c.GenreIds.ShouldBe([18]));
    }

    [Fact]
    public async Task Spotify_FindTracksSearchesTitleByArtist()
    {
        var handler = new StubHttpHandler(request => request.RequestUri!.Host == "accounts.spotify.com"
            ? StubHttpHandler.Json(SpotifyToken)
            : StubHttpHandler.Json("""{ "tracks": { "items": [ { "id": "a", "name": "Hells Bells", "popularity": 77, "album": { "name": "Back in Black" } } ] } }"""));
        var service = new SpotifyService(new HttpClient(handler), Config);

        var tracks = await service.FindTracksAsync("Back in Black", "AC/DC", CancellationToken.None);

        tracks.Single().ShouldSatisfyAllConditions(t => t.Popularity.ShouldBe(77), t => t.Album!.Name.ShouldBe("Back in Black"));
        handler.Requests[1].RequestUri!.AbsoluteUri
            .ShouldBe("https://api.spotify.com/v1/search?q=Back%20in%20Black%20artist%3AAC%2FDC&type=track&limit=10");
    }

    [Fact]
    public async Task Spotify_FindTracksThrowsOnFailure_IncludingTheTokenRequest()
    {
        var limited = new SpotifyService(new HttpClient(new StubHttpHandler(request => request.RequestUri!.Host == "accounts.spotify.com"
            ? StubHttpHandler.Json(SpotifyToken)
            : new HttpResponseMessage(HttpStatusCode.TooManyRequests))), Config);
        var noToken = new SpotifyService(new HttpClient(new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))), Config);

        await Should.ThrowAsync<MediaSourceUnavailableException>(() => limited.FindTracksAsync("x", null, CancellationToken.None));
        await Should.ThrowAsync<MediaSourceUnavailableException>(() => noToken.FindTracksAsync("x", null, CancellationToken.None));
    }

    [Fact]
    public async Task Spotify_TokenIsReusedUntilItExpires()
    {
        var handler = new StubHttpHandler(request => request.RequestUri!.Host == "accounts.spotify.com"
            ? StubHttpHandler.Json("""{ "access_token": "long-lived", "expires_in": 3600 }""")
            : StubHttpHandler.Json("""{ "tracks": { "items": [] } }"""));
        var cache = new SpotifyTokenCache();
        // Two service instances, like two requests: the cache is the shared singleton
        var first = new SpotifyService(new HttpClient(handler), Config, cache);
        var second = new SpotifyService(new HttpClient(handler), Config, cache);

        await first.SearchMusicAsync("a");
        await second.FindTracksAsync("b", null, CancellationToken.None);

        handler.Requests.Count(r => r.RequestUri!.Host == "accounts.spotify.com").ShouldBe(1);
        handler.Requests.Last().Headers.Authorization!.ToString().ShouldBe("Bearer long-lived");
    }

    [Fact]
    public async Task Spotify_FailedDataRequest_ReturnsEmptyResult()
    {
        var handler = new StubHttpHandler(request => request.RequestUri!.Host == "accounts.spotify.com"
            ? StubHttpHandler.Json(SpotifyToken)
            : new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var service = new SpotifyService(new HttpClient(handler), Config);

        (await service.SearchMusicAsync("x")).Items.ShouldBeEmpty();
    }
}
