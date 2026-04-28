using ReviewApp.Api.DTOs;
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
        return await FetchFromTmdbAsync($"search/movie?query={Uri.EscapeDataString(query)}&page={page}");
    }

    public async Task<TmdbSearchResponseDto> SearchSeriesAsync(string query, int page = 1)
    {
        return await FetchFromTmdbAsync($"search/tv?query={Uri.EscapeDataString(query)}&page={page}");
    }

    // Helper method to fetch data from TMDb API
    private async Task<TmdbSearchResponseDto> FetchFromTmdbAsync(string endpoint)
    {
        var apiKey = _config["Tmdb:ApiKey"];
        var response = await _httpClient.GetAsync($"{endpoint}&api_key={apiKey}");

        if (!response.IsSuccessStatusCode)
        {
            return new TmdbSearchResponseDto { Results = [], TotalCount = 0 };
        }

        var jsonString = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<TmdbSearchResponseDto>(jsonString) ?? new TmdbSearchResponseDto { Results = [], TotalCount = 0 };
    }
}