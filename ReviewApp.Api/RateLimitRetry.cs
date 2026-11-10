namespace ReviewApp.Api;

/// <summary>
/// The windows of the rate limit policies (<c>Program.cs</c>), and the <c>Retry-After</c> derived from them.
/// Sliding window leases carry no <c>RetryAfter</c> metadata; the oldest segment of a full window frees up within
/// one segment, so that is when to retry. The global limit applies to every request too, so the longer of the two
/// windows wins.
/// </summary>
public static class RateLimitRetry
{
    /// <summary>How many segments a sliding window is split into; one segment is the retry delay.</summary>
    public const int SegmentsPerWindow = 5;

    public static readonly TimeSpan GlobalWindow = TimeSpan.FromMinutes(1);

    /// <summary>The window of each named sliding-window policy. Keep in sync with the policies in <c>Program.cs</c>. The token-bucket <c>sync</c> policy is absent because its lease carries its own <c>Retry-After</c>.</summary>
    public static readonly IReadOnlyDictionary<string, TimeSpan> Windows = new Dictionary<string, TimeSpan>
    {
        ["auth-attempt"] = TimeSpan.FromMinutes(10),
        ["credential-change"] = TimeSpan.FromMinutes(10),
        ["email-link"] = TimeSpan.FromMinutes(10),
        ["email-send"] = TimeSpan.FromMinutes(10),
        ["external-api"] = TimeSpan.FromMinutes(1),
        ["billing"] = TimeSpan.FromMinutes(10),
        ["recognition"] = TimeSpan.FromMinutes(10)
    };

    /// <summary>Calculates when a rejected request may be retried.</summary>
    /// <param name="policyName">The endpoint's policy, or null when only the global limit applies.</param>
    /// <returns>One segment of the longer of the policy's and the global window.</returns>
    public static TimeSpan For(string? policyName)
    {
        var window = policyName != null && Windows.TryGetValue(policyName, out var policyWindow) && policyWindow > GlobalWindow
            ? policyWindow
            : GlobalWindow;
        return window / SegmentsPerWindow;
    }
}
