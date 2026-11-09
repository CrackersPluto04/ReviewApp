namespace ReviewApp.Api.Tests.Unit;

// F4: the Retry-After of a sliding window rejection is one segment of the longer window that applies
public class RateLimitRetryTests
{
    [Theory]
    [InlineData("auth-attempt", 120)]
    [InlineData("billing", 120)]
    [InlineData("external-api", 12)]
    [InlineData(null, 12)]
    [InlineData("unknown-policy", 12)]
    public void RetryAfter_IsOneSegmentOfTheLongerWindow(string? policy, int seconds)
    {
        RateLimitRetry.For(policy).ShouldBe(TimeSpan.FromSeconds(seconds));
    }
}
