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

    public async Task<List<TmdbItemDto>> SearchMoviesAsync(string query)
    {
        return await FetchFromTmdbAsync($"search/movie?query={Uri.EscapeDataString(query)}");
    }

    public async Task<List<TmdbItemDto>> SearchSeriesAsync(string query)
    {
        return await FetchFromTmdbAsync($"search/tv?query={Uri.EscapeDataString(query)}");
    }


    // Helper method to fetch data from TMDb API
    private async Task<List<TmdbItemDto>> FetchFromTmdbAsync(string endpoint)
    {
        var apiKey = _config["Tmdb:ApiKey"];
        var response = await _httpClient.GetAsync($"{endpoint}&api_key={apiKey}");

        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        var jsonString = await response.Content.ReadAsStringAsync();
        var tmdbData = JsonSerializer.Deserialize<TmdbSearchResponseDto>(jsonString);

        return tmdbData?.Results ?? [];
    }
}