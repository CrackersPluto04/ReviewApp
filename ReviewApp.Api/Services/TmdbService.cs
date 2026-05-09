using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using System.Text.Json;

namespace ReviewApp.Api.Services;

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

    /* Helper methods */

    // Generic method to fetch data from TMDb and deserialize it into the specified type
    private async Task<T> FetchFromTmdbAsync<T>(string endpoint) where T : class, new()
    {
        var apiKey = _config["Tmdb:ApiKey"];
        var separator = endpoint.Contains('?') ? "&" : "?";
        var response = await _httpClient.GetAsync($"{endpoint}{separator}api_key={apiKey}");

        if (!response.IsSuccessStatusCode)
        {
            return new T();
        }

        var jsonString = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(jsonString) ?? new T();
    }

    // Helper method to build query string for discover endpoints
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