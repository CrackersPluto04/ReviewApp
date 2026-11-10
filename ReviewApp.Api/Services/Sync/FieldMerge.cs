namespace ReviewApp.Api.Services.Sync;

/// <summary>
/// The result of a three-way merge.
/// </summary>
/// <typeparam name="T">The merged fields type.</typeparam>
/// <param name="Value">The merged values. When there are conflicts it must not be written: the user decides.</param>
/// <param name="Conflicts">The names of the fields both sides changed differently.</param>
/// <param name="KeptServerChanges">Whether a change made elsewhere was kept in the result, which makes the outcome "merged" rather than "applied".</param>
public record MergeOutcome<T>(T Value, IReadOnlyList<string> Conflicts, bool KeptServerChanges)
{
    public bool HasConflicts => Conflicts.Count > 0;
}

/// <summary>
/// Three-way merge of an offline edit: <c>base</c> is what the device's edit started from, <c>mine</c> the
/// device's values, <c>server</c> the current values. A field only one side changed takes that side's value; a
/// field both changed to the same value is fine; a field both changed differently is a conflict, and the server's
/// value is kept in the result. Texts treat null and an empty string as the same, so clearing a field on one side
/// is not mistaken for a change.
/// </summary>
public static class FieldMerge
{
    /// <summary>Merges the fields of a review: score, text, pros, cons and visibility.</summary>
    /// <param name="base">The values the device's edit started from.</param>
    /// <param name="mine">The device's values.</param>
    /// <param name="server">The current server values.</param>
    public static MergeOutcome<ReviewFields> Review(ReviewFields @base, ReviewFields mine, ReviewFields server)
    {
        var merge = new Merger();
        var result = new ReviewFields(
            merge.Field("score", @base.Score, mine.Score, server.Score),
            merge.Text("reviewText", @base.ReviewText, mine.ReviewText, server.ReviewText),
            merge.Text("pros", @base.Pros, mine.Pros, server.Pros),
            merge.Text("cons", @base.Cons, mine.Cons, server.Cons),
            merge.Field("visibility", @base.VisibilityLevel, mine.VisibilityLevel, server.VisibilityLevel));
        return merge.Outcome(result);
    }

    /// <summary>Merges the fields of a collection: name and visibility.</summary>
    /// <param name="base">The values the device's edit started from.</param>
    /// <param name="mine">The device's values.</param>
    /// <param name="server">The current server values.</param>
    public static MergeOutcome<CollectionFields> Collection(CollectionFields @base, CollectionFields mine, CollectionFields server)
    {
        var merge = new Merger();
        var result = new CollectionFields(
            merge.Field("name", @base.Name, mine.Name, server.Name),
            merge.Field("visibility", @base.Visibility, mine.Visibility, server.Visibility));
        return merge.Outcome(result);
    }

    private sealed class Merger
    {
        private readonly List<string> _conflicts = [];
        private bool _keptServer;

        public T Field<T>(string name, T @base, T mine, T server) => Merge(name, @base, mine, server, EqualityComparer<T>.Default.Equals);

        public string? Text(string name, string? @base, string? mine, string? server) =>
            Merge(name, @base, mine, server, (a, b) => string.Equals(EmptyAsNull(a), EmptyAsNull(b), StringComparison.Ordinal));

        public MergeOutcome<T> Outcome<T>(T value) => new(value, _conflicts, _keptServer);

        private T Merge<T>(string name, T @base, T mine, T server, Func<T, T, bool> same)
        {
            var mineChanged = !same(mine, @base);
            var serverChanged = !same(server, @base);

            if (!mineChanged)
            {
                _keptServer |= serverChanged;
                return server;
            }
            if (serverChanged && !same(mine, server))
            {
                _conflicts.Add(name);
                return server;
            }
            return mine;
        }

        private static string? EmptyAsNull(string? text) => string.IsNullOrEmpty(text) ? null : text;
    }
}

/// <summary>
/// Decides when an offline-made review was written. The device's clock is not trusted: its time is used only if it
/// is within the last <see cref="MaxAge"/> and not in the future (<see cref="MaxSkew"/> allows for a slightly fast
/// device clock); otherwise the server's time is used.
/// </summary>
public static class ClientTimestamp
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);
    public static readonly TimeSpan MaxSkew = TimeSpan.FromMinutes(2);

    /// <summary>Picks the creation time to record.</summary>
    /// <param name="client">The device's time, if it sent one.</param>
    /// <param name="utcNow">The server's current time, UTC.</param>
    /// <returns>The device's time when plausible (a time slightly in the future is clamped to now), else <paramref name="utcNow"/>.</returns>
    public static DateTime Accept(DateTimeOffset? client, DateTime utcNow)
    {
        if (client == null)
            return utcNow;

        var time = client.Value.UtcDateTime;
        if (time < utcNow - MaxAge || time > utcNow + MaxSkew)
            return utcNow;
        return time > utcNow ? utcNow : time;
    }
}
