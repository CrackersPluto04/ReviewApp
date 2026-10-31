using System.Net;
using Microsoft.Extensions.Configuration;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Services;
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
