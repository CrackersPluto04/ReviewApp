namespace ReviewApp.Api.Services.Sync;

// The result of a three-way merge: the merged values, the fields both sides changed differently (then Value is
// not to be written, the user decides), and whether a change made elsewhere was kept.
public record MergeOutcome<T>(T Value, IReadOnlyList<string> Conflicts, bool KeptServerChanges)
{
    public bool HasConflicts => Conflicts.Count > 0;
}

// Three-way merge of an offline edit: base = what the device's edit started from, mine = the device's values,
// server = the current values. A field only one side changed takes that side's value; a field both changed to the
// same value is fine; a field both changed differently is a conflict. Texts treat null and "" as the same.
public static class FieldMerge
{
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

// When an offline-made review was written. The device's time is used only if it is within the last
// MaxAge and not in the future (MaxSkew allows for a slightly fast device clock); otherwise the server's time.
public static class ClientTimestamp
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);
    public static readonly TimeSpan MaxSkew = TimeSpan.FromMinutes(2);

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
