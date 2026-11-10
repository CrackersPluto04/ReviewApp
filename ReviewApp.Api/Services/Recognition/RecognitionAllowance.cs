namespace ReviewApp.Api.Services.Recognition;

/// <summary>A user's daily recognition allowance at one moment.</summary>
/// <param name="Limit">Recognitions allowed per day.</param>
/// <param name="Used">Recognitions counted since the day started.</param>
/// <param name="Remaining">Recognitions left today, never negative.</param>
/// <param name="ResetsAt">The next UTC midnight, UTC.</param>
public readonly record struct AllowanceState(int Limit, int Used, int Remaining, DateTime ResetsAt)
{
    public bool IsExhausted => Remaining <= 0;
}

/// <summary>
/// The daily allowance: recognitions since the start of the current UTC day, reset at the next UTC midnight. The
/// count is derived from stored usage rows rather than kept as a counter, so there is nothing to reset.
/// </summary>
public static class RecognitionAllowance
{
    /// <summary>The start of the UTC day containing <paramref name="nowUtc"/>.</summary>
    public static DateTime DayStart(DateTime nowUtc) => DateTime.SpecifyKind(nowUtc.Date, DateTimeKind.Utc);

    /// <summary>Computes the allowance, clamping a negative limit or count to zero.</summary>
    /// <param name="limit">Recognitions allowed per day.</param>
    /// <param name="usedToday">Recognitions counted since <see cref="DayStart"/>.</param>
    /// <param name="nowUtc">The current time, UTC.</param>
    public static AllowanceState Compute(int limit, int usedToday, DateTime nowUtc)
    {
        limit = Math.Max(0, limit);
        usedToday = Math.Max(0, usedToday);
        return new AllowanceState(limit, usedToday, Math.Max(0, limit - usedToday), DayStart(nowUtc).AddDays(1));
    }
}
