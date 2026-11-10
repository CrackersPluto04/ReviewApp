using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace ReviewApp.Api.Services.Recognition;

/// <summary>
/// The ranked lists behind "Show more": in memory, short-lived, bound to the user who made the recognition. Holds
/// media references and tiers only, never the photo or the AI's answer. Lost on restart, in which case the client
/// offers a retake. Being in memory it assumes a single API instance.
/// </summary>
public class RecognitionResultStore
{
    private const string KeyPrefix = "recognition-result:";
    private const int MaxIdLength = 64;

    private readonly IMemoryCache _cache;
    private readonly IOptions<RecognitionOptions> _options;

    public RecognitionResultStore(IMemoryCache cache, IOptions<RecognitionOptions> options)
    {
        _cache = cache;
        _options = options;
    }

    private sealed record Entry(int UserId, IReadOnlyList<RankedItem> Items);

    /// <summary>Stores a list for the user and returns its ID: 128 random bits, so it cannot be guessed.</summary>
    public string Put(int userId, IReadOnlyList<RankedItem> items)
    {
        var id = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(16));
        _cache.Set(KeyPrefix + id, new Entry(userId, items), TimeSpan.FromMinutes(_options.Value.ResultTtlMinutes));
        return id;
    }

    /// <summary>Returns a stored list.</summary>
    /// <param name="id">The ID from <see cref="Put"/>.</param>
    /// <param name="userId">The user asking; it must be the one who made the recognition.</param>
    /// <returns>The list, or null for an unknown, expired or someone else's recognition. The caller cannot tell which.</returns>
    public IReadOnlyList<RankedItem>? Get(string? id, int userId)
    {
        if (string.IsNullOrEmpty(id) || id.Length > MaxIdLength)
            return null;

        return _cache.TryGetValue(KeyPrefix + id, out Entry? entry) && entry!.UserId == userId ? entry.Items : null;
    }
}

/// <summary>
/// One recognition at a time per user: a double tap or a retry while one is running is refused and never counted
/// twice. In memory, so it assumes a single API instance.
/// </summary>
public class RecognitionGate
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, byte> _running = new();

    /// <summary>Tries to start a recognition for the user.</summary>
    /// <returns>A handle to dispose when the recognition ends, or null when one is already running.</returns>
    public IDisposable? TryEnter(int userId) => _running.TryAdd(userId, 0) ? new Release(_running, userId) : null;

    private sealed class Release : IDisposable
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, byte> _running;
        private readonly int _userId;
        private int _released;

        public Release(System.Collections.Concurrent.ConcurrentDictionary<int, byte> running, int userId)
        {
            _running = running;
            _userId = userId;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                _running.TryRemove(_userId, out _);
        }
    }
}
