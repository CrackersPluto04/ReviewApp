namespace ReviewApp.Api.Tests.Support;

public static class TestWait
{
    // For effects of background work (the email dispatcher); fails instead of hanging
    public static async Task UntilAsync(Func<bool> condition, int timeoutMs = 10_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The condition was not met in time.");
            await Task.Delay(25);
        }
    }
}
