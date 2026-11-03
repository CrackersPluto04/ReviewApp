using ReviewApp.Api.DTOs;
using ReviewApp.Api.Services.Interfaces;
using ReviewApp.Api.Services.Recognition;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ReviewApp.Api.Services;

public class SpotifyService : ISpotifyService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly SpotifyTokenCache _tokenCache;

    // The token cache is a singleton in the app (typed clients are created per use); without one, every call fetches a new token
    public SpotifyService(HttpClient httpClient, IConfiguration config, SpotifyTokenCache? tokenCache = null)
    {
        _httpClient = httpClient;
        _config = config;
        _tokenCache = tokenCache ?? new SpotifyTokenCache();
    }

    public async Task<SpotifyTracksDto> SearchMusicAsync(string query, int page = 1)
    {
        var url = $"https://api.spotify.com/v1/search?q={Uri.EscapeDataString(query)}&type=track&limit=10&offset={(page - 1) * 10}";
        var result = await FetchFromSpotifyAsync<SpotifySearchResponseDto>(url);
        return result.Tracks;
    }

    public async Task<SpotifyTracksDto> DiscoverMusicAsync(SpotifyParams p)
    {
        // Build the query string based on the provided parameters
        var queryParts = new List<string>
        {
            $"genre:{p.Genre.ToLower()}"
        };
        if (!string.IsNullOrWhiteSpace(p.Year))
            queryParts.Add($"year:{p.Year}");

        var queryString = string.Join(" ", queryParts);

        var url = $"https://api.spotify.com/v1/search?q={Uri.EscapeDataString(queryString)}&type=track&market={Uri.EscapeDataString(p.Market)}&limit=10&offset={(p.Page - 1) * 10}";
        var result = await FetchFromSpotifyAsync<SpotifySearchResponseDto>(url);
        return result.Tracks;
    }

    public async Task<SpotifyTrackDto> GetMusicByIdAsync(string id)
    {
        var url = $"https://api.spotify.com/v1/tracks/{Uri.EscapeDataString(id)}";
        return await FetchFromSpotifyAsync<SpotifyTrackDto>(url);
    }

    public async Task<List<SpotifyTrackDto>> FindTracksAsync(string title, string? artist, CancellationToken cancellationToken)
    {
        // No field filter on the title: it matches both track and album names (the AI names albums and singles)
        var query = string.IsNullOrWhiteSpace(artist) ? title : $"{title} artist:{artist}";
        var url = $"https://api.spotify.com/v1/search?q={Uri.EscapeDataString(query)}&type=track&limit=10";

        try
        {
            var token = await _tokenCache.GetAsync(GetAccessTokenAsync, cancellationToken);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new MediaSourceUnavailableException("Spotify");

            var jsonString = await response.Content.ReadAsStringAsync(cancellationToken);
            return (JsonSerializer.Deserialize<SpotifySearchResponseDto>(jsonString) ?? new()).Tracks.Items;
        }
        catch (HttpRequestException ex)
        {
            throw new MediaSourceUnavailableException("Spotify", ex);
        }
        catch (JsonException ex)
        {
            throw new MediaSourceUnavailableException("Spotify", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MediaSourceUnavailableException("Spotify", ex);
        }
    }

    /* Helper methods */

    // Generic method to fetch data from Spotify API and deserialize it into the specified type
    private async Task<T> FetchFromSpotifyAsync<T>(string url) where T : class, new()
    {
        var token = await _tokenCache.GetAsync(GetAccessTokenAsync, CancellationToken.None);

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return new T();
        }
        var jsonString = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(jsonString) ?? new T();
    }

    // Helper method to get Spotify access token using Client Credentials flow
    private async Task<SpotifyTokenDto> GetAccessTokenAsync(CancellationToken cancellationToken)
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

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var jsonString = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<SpotifyTokenDto>(jsonString) ?? new SpotifyTokenDto();
    }
}

// Keeps the client-credentials token until shortly before it expires (Spotify issues them for an hour),
// instead of fetching a new one for every call
public class SpotifyTokenCache
{
    private static readonly TimeSpan Margin = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTime _expiresAt;

    public async Task<string> GetAsync(Func<CancellationToken, Task<SpotifyTokenDto>> fetch, CancellationToken cancellationToken)
    {
        if (_token != null && DateTime.UtcNow < _expiresAt)
            return _token;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_token != null && DateTime.UtcNow < _expiresAt)
                return _token;

            var fresh = await fetch(cancellationToken);
            // A token without a lifetime is used once and not kept
            if (!string.IsNullOrEmpty(fresh.AccessToken) && fresh.ExpiresIn > Margin.TotalSeconds)
            {
                _token = fresh.AccessToken;
                _expiresAt = DateTime.UtcNow.AddSeconds(fresh.ExpiresIn) - Margin;
            }
            return fresh.AccessToken;
        }
        finally
        {
            _lock.Release();
        }
    }
}
