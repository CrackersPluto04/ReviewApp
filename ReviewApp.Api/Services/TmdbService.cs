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

    public async Task<List<TmdbMovieDto>> SearchMoviesAsync(string query)
    {
        var apiKey = _config["Tmdb:ApiKey"];
        var response = await _httpClient.GetAsync($"search/movie?api_key={apiKey}&query={Uri.EscapeDataString(query)}");

        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        var jsonString = await response.Content.ReadAsStringAsync();
        var tmdbData = JsonSerializer.Deserialize<TmdbSearchResponseDto>(jsonString);

        return tmdbData?.Results ?? [];
    }
}
