using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services.Interfaces;
using ReviewApp.Api.Services.Recognition;
using System.Text.Json;

namespace ReviewApp.Api.Services;

/// <summary>
/// TMDb client. There are two failure styles on purpose. The search, discover and details methods are lenient: any
/// failure becomes an empty result, which is fine for browsing. The strict methods (<c>Find*</c>, used before
/// storing media and by camera recognition) treat only "not found" as empty and throw
/// <see cref="MediaSourceUnavailableException"/> for everything else, so an outage is never mistaken for "does not exist".
/// </summary>
public class TmdbService : ITmdbService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;

    public TmdbService(HttpClient httpClient, IConfiguration config)
    {
        _httpClient = httpClient;
        _config = config;

        _httpClient.BaseAddress = new Uri(_config["Tmdb:BaseUrl"]!);
    }

    public async Task<TmdbSearchResponseDto> SearchMoviesAsync(string query, int page = 1)
    {
        return await FetchFromTmdbAsync<TmdbSearchResponseDto>($"search/movie?query={Uri.EscapeDataString(query)}&page={page}");
    }

    public async Task<TmdbSearchResponseDto> SearchSeriesAsync(string query, int page = 1)
    {
        return await FetchFromTmdbAsync<TmdbSearchResponseDto>($"search/tv?query={Uri.EscapeDataString(query)}&page={page}");
    }

    public async Task<TmdbSearchResponseDto> DiscoverMoviesAsync(TmdbParams p)
    {
        var queryString = BuildDiscoverQueryString(p, MediaType.Movie);
        return await FetchFromTmdbAsync<TmdbSearchResponseDto>($"discover/movie?{queryString}");
    }

    public async Task<TmdbSearchResponseDto> DiscoverSeriesAsync(TmdbParams p)
    {
        var queryString = BuildDiscoverQueryString(p, MediaType.Series);
        return await FetchFromTmdbAsync<TmdbSearchResponseDto>($"discover/tv?{queryString}");
    }

    public async Task<TmdbItemDto> GetMovieByIdAsync(string id)
    {
        return await FetchFromTmdbAsync<TmdbItemDto>($"movie/{Uri.EscapeDataString(id)}");
    }

    public async Task<TmdbItemDto> GetSeriesByIdAsync(string id)
    {
        return await FetchFromTmdbAsync<TmdbItemDto>($"tv/{Uri.EscapeDataString(id)}");
    }

    /* Strict lookups by ID (an unknown ID is null, failures throw MediaSourceUnavailableException) */

    public async Task<TmdbItemDto?> FindMovieByIdAsync(string id, CancellationToken cancellationToken) =>
        Known(await FetchStrictAsync<TmdbItemDto>($"movie/{Uri.EscapeDataString(id)}", cancellationToken, badRequestIsNotFound: true));

    public async Task<TmdbItemDto?> FindSeriesByIdAsync(string id, CancellationToken cancellationToken) =>
        Known(await FetchStrictAsync<TmdbItemDto>($"tv/{Uri.EscapeDataString(id)}", cancellationToken, badRequestIsNotFound: true));

    // TMDb's "not found" (and the empty item of a 404) has no ID and no title
    private static TmdbItemDto? Known(TmdbItemDto item) =>
        item.ID <= 0 || (item.Title ?? item.Name) is null ? null : item;

    /* Camera recognition lookups (strict: failures throw MediaSourceUnavailableException) */

    public async Task<List<TmdbItemDto>> FindMoviesAsync(string title, CancellationToken cancellationToken)
    {
        var result = await FetchStrictAsync<TmdbSearchResponseDto>($"search/movie?query={Uri.EscapeDataString(title)}&include_adult=false", cancellationToken);
        return result.Results.Where(r => !r.Adult).ToList();
    }

    public async Task<List<TmdbItemDto>> FindSeriesAsync(string title, CancellationToken cancellationToken)
    {
        var result = await FetchStrictAsync<TmdbSearchResponseDto>($"search/tv?query={Uri.EscapeDataString(title)}&include_adult=false", cancellationToken);
        return result.Results.Where(r => !r.Adult).ToList();
    }

    public async Task<int?> GetMovieCollectionIdAsync(int movieId, CancellationToken cancellationToken)
    {
        var movie = await FetchStrictAsync<TmdbItemDto>($"movie/{movieId}", cancellationToken);
        return movie.BelongsToCollection?.ID;
    }

    public async Task<List<TmdbItemDto>> GetCollectionPartsAsync(int collectionId, CancellationToken cancellationToken)
    {
        var collection = await FetchStrictAsync<TmdbCollectionDto>($"collection/{collectionId}", cancellationToken);
        return collection.Parts.Where(p => !p.Adult).ToList();
    }

    public async Task<List<TmdbPersonDto>> FindPeopleAsync(string name, CancellationToken cancellationToken)
    {
        var result = await FetchStrictAsync<TmdbPersonSearchResponseDto>($"search/person?query={Uri.EscapeDataString(name)}&include_adult=false", cancellationToken);
        return result.Results.Where(r => !r.Adult).ToList();
    }

    public async Task<TmdbPersonCreditsDto> GetPersonCreditsAsync(int personId, CancellationToken cancellationToken)
    {
        var credits = await FetchStrictAsync<TmdbPersonCreditsDto>($"person/{personId}/combined_credits", cancellationToken);
        credits.Cast.RemoveAll(c => c.Adult);
        credits.Crew.RemoveAll(c => c.Adult);
        return credits;
    }

    /* Helper methods */

    // Lenient fetch: any failure is an empty result
    private async Task<T> FetchFromTmdbAsync<T>(string endpoint) where T : class, new()
    {
        var response = await _httpClient.GetAsync(WithApiKey(endpoint));

        if (!response.IsSuccessStatusCode)
        {
            return new T();
        }

        var jsonString = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(jsonString) ?? new T();
    }

    // Like FetchFromTmdbAsync, but only "not found" is an empty result; any other failure throws.
    // Lookups by ID also treat 400 as not found (TMDb answers it for malformed IDs)
    private async Task<T> FetchStrictAsync<T>(string endpoint, CancellationToken cancellationToken, bool badRequestIsNotFound = false) where T : class, new()
    {
        try
        {
            using var response = await _httpClient.GetAsync(WithApiKey(endpoint), cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound
                || (badRequestIsNotFound && response.StatusCode == System.Net.HttpStatusCode.BadRequest))
                return new T();
            if (!response.IsSuccessStatusCode)
                throw new MediaSourceUnavailableException("TMDb");

            var jsonString = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<T>(jsonString) ?? new T();
        }
        catch (HttpRequestException ex)
        {
            throw new MediaSourceUnavailableException("TMDb", ex);
        }
        catch (JsonException ex)
        {
            throw new MediaSourceUnavailableException("TMDb", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient's own timeout
            throw new MediaSourceUnavailableException("TMDb", ex);
        }
    }

    private string WithApiKey(string endpoint)
    {
        var separator = endpoint.Contains('?') ? "&" : "?";
        return $"{endpoint}{separator}api_key={_config["Tmdb:ApiKey"]}";
    }

    // The year filter has a different name for movies and series
    private string BuildDiscoverQueryString(TmdbParams p, MediaType type)
    {
        var queryParams = new List<string>
        {
            $"page={p.Page}",
            $"sort_by={Uri.EscapeDataString(p.SortBy)}"
        };

        if (p.Year.HasValue && type == MediaType.Movie)
            queryParams.Add($"primary_release_year={p.Year.Value}");
        else if (p.Year.HasValue && type == MediaType.Series)
            queryParams.Add($"first_air_date_year={p.Year.Value}");

        if (!string.IsNullOrEmpty(p.WithGenres))
            queryParams.Add($"with_genres={Uri.EscapeDataString(p.WithGenres)}");

        if (p.MinRuntime.HasValue)
            queryParams.Add($"with_runtime.gte={p.MinRuntime.Value}");

        if (p.MaxRuntime.HasValue)
            queryParams.Add($"with_runtime.lte={p.MaxRuntime.Value}");

        return string.Join("&", queryParams);
    }
}