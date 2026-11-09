namespace ReviewApp.Api;

// The windows of the rate limit policies (Program.cs), and the Retry-After derived from them.
// Sliding window leases carry no RetryAfter metadata; the oldest segment of a full window frees up within one
// segment, so that is when to retry. The global limit applies to every request too, so the longer of the two wins.
public static class RateLimitRetry
{
    public const int SegmentsPerWindow = 5;

    public static readonly TimeSpan GlobalWindow = TimeSpan.FromMinutes(1);

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

    // When a rejected request may be retried, for the endpoint's policy (null = only the global limit applies)
    public static TimeSpan For(string? policyName)
    {
        var window = policyName != null && Windows.TryGetValue(policyName, out var policyWindow) && policyWindow > GlobalWindow
            ? policyWindow
            : GlobalWindow;
        return window / SegmentsPerWindow;
    }
}
