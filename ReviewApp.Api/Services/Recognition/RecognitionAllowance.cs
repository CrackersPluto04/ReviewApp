namespace ReviewApp.Api.Services.Recognition;

public readonly record struct AllowanceState(int Limit, int Used, int Remaining, DateTime ResetsAt)
{
    public bool IsExhausted => Remaining <= 0;
}

// The daily allowance: recognitions since the start of the current UTC day, reset at the next UTC midnight
public static class RecognitionAllowance
{
    public static DateTime DayStart(DateTime nowUtc) => DateTime.SpecifyKind(nowUtc.Date, DateTimeKind.Utc);

    public static AllowanceState Compute(int limit, int usedToday, DateTime nowUtc)
    {
        limit = Math.Max(0, limit);
        usedToday = Math.Max(0, usedToday);
        return new AllowanceState(limit, usedToday, Math.Max(0, limit - usedToday), DayStart(nowUtc).AddDays(1));
    }
}
