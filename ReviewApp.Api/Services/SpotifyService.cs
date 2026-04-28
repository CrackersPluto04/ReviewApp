using ReviewApp.Api.DTOs;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ReviewApp.Api.Services;

public class SpotifyService : ISpotifyService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;

    public SpotifyService(HttpClient httpClient, IConfiguration config)
    {
        _httpClient = httpClient;
        _config = config;
    }

    public async Task<SpotifyTracksDto> SearchMusicAsync(string query, int page = 1)
    {
        var token = await GetAccessTokenAsync();
        var url = $"https://api.spotify.com/v1/search?q={Uri.EscapeDataString(query)}&type=track&limit=10&offset={(page - 1) * 10}";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            //var errorBody = await response.Content.ReadAsStringAsync();
            //Console.WriteLine($"SPOTIFY ERROR: {errorBody}");

            return new SpotifyTracksDto { TotalCount = 0, Items = [] };
        }

        var jsonString = await response.Content.ReadAsStringAsync();
        var searchData = JsonSerializer.Deserialize<SpotifySearchResponseDto>(jsonString);

        return searchData?.Tracks ?? new SpotifyTracksDto { TotalCount = 0, Items = [] };
    }

    // Helper method to get Spotify access token using Client Credentials flow
    private async Task<string> GetAccessTokenAsync()
    {
        var clientId = _config["Spotify:ClientId"];
        var clientSecret = _config["Spotify:ClientSecret"];

        var authBytes = Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}");
        var base64Auth = Convert.ToBase64String(authBytes);

        var request = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", base64Auth);
        request.Content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("grant_type", "client_credentials")
        ]);

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var jsonString = await response.Content.ReadAsStringAsync();
        var tokenData = JsonSerializer.Deserialize<SpotifyTokenDto>(jsonString);

        return tokenData?.AccessToken ?? string.Empty;
    }
}
