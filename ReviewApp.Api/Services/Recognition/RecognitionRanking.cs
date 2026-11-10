using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;

namespace ReviewApp.Api.Services.Recognition;

/// <summary>A real media item found for the recognition, before ranking.</summary>
/// <param name="Media">The item.</param>
/// <param name="Tier">How it was reached: directly named, same universe, connected people, or merely similar.</param>
/// <param name="Confidence">The AI's confidence in the work it came from, 0 to 1.</param>
/// <param name="Popularity">How well known it is at its source, used to break ties.</param>
public record RankCandidate(MediaDto Media, RecognitionTier Tier, double Confidence, double Popularity);

/// <summary>A ranked suggestion; the list position is its rank.</summary>
public record RankedItem(MediaDto Media, RecognitionTier Tier);

/// <summary>One page of a ranked list.</summary>
/// <param name="Items">The items of this page.</param>
/// <param name="NextOffset">The offset of the next page.</param>
/// <param name="HasMore">Whether more items follow.</param>
/// <param name="Total">The size of the whole list.</param>
public record RankedPage(IReadOnlyList<RankedItem> Items, int NextOffset, bool HasMore, int Total);

/// <summary>
/// Orders the suggestions of one recognition: closest tier first, then the AI's confidence, then popularity. The
/// order is deterministic, so paging over the stored list never repeats or skips an item.
/// </summary>
public static class RecognitionRanking
{
    // Confidences this close count as equal, so popularity decides between them (0.82 vs 0.84 means nothing)
    private const double ConfidenceBucket = 0.05;

    /// <summary>
    /// Filters, de-duplicates and orders the candidates. Drops low-confidence ones, keeps the closest tier of an item
    /// reached through several relations, merges re-releases, uses the loosest tier only to fill a thin list, and caps the total.
    /// </summary>
    /// <param name="candidates">Everything the resolver found.</param>
    /// <param name="options">The thresholds and caps.</param>
    public static IReadOnlyList<RankedItem> Rank(IEnumerable<RankCandidate> candidates, RecognitionOptions options)
    {
        var kept = candidates
            .Where(c => c.Confidence >= options.MinConfidence)
            // The same item reached through several relations keeps its closest one
            .GroupBy(c => (c.Media.MediaType, c.Media.ExternalApiID))
            .Select(g => g.OrderBy(c => c, Preference).First())
            // Re-releases and duplicates with another ID: same type, title, year and artist
            .GroupBy(DuplicateKey)
            .Select(g => g.OrderBy(c => c, Preference).First())
            .ToList();

        // The loosest tier only fills a thin list
        if (kept.Count(c => c.Tier != RecognitionTier.Similar) >= options.SimilarFillThreshold)
            kept.RemoveAll(c => c.Tier == RecognitionTier.Similar);

        return kept
            .OrderBy(c => c, Preference)
            .Take(Math.Max(0, options.TotalCap))
            .Select(c => new RankedItem(c.Media, c.Tier))
            .ToList();
    }

    /// <summary>Takes one page of a ranked list. An out-of-range offset or count is clamped.</summary>
    /// <param name="items">The whole ranked list.</param>
    /// <param name="offset">Index of the first item.</param>
    /// <param name="count">How many items to take.</param>
    public static RankedPage Page(IReadOnlyList<RankedItem> items, int offset, int count)
    {
        offset = Math.Clamp(offset, 0, items.Count);
        count = Math.Max(0, count);
        var page = items.Skip(offset).Take(count).ToList();
        var next = offset + page.Count;
        return new RankedPage(page, next, next < items.Count, items.Count);
    }

    // Tier, confidence bucket (higher first), popularity (higher first), release date (older first, so franchises
    // read in order), title, then the external ID as the last word
    private static readonly IComparer<RankCandidate> Preference = Comparer<RankCandidate>.Create((a, b) =>
    {
        var result = a.Tier.CompareTo(b.Tier);
        if (result != 0) return result;

        result = Bucket(b.Confidence).CompareTo(Bucket(a.Confidence));
        if (result != 0) return result;

        result = b.Popularity.CompareTo(a.Popularity);
        if (result != 0) return result;

        result = CompareReleaseDates(a.Media.ReleaseDate, b.Media.ReleaseDate);
        if (result != 0) return result;

        result = string.Compare(a.Media.Title, b.Media.Title, StringComparison.OrdinalIgnoreCase);
        if (result != 0) return result;

        result = a.Media.MediaType.CompareTo(b.Media.MediaType);
        return result != 0 ? result : string.CompareOrdinal(a.Media.ExternalApiID, b.Media.ExternalApiID);
    });

    private static (MediaType, string, int?, string) DuplicateKey(RankCandidate candidate)
    {
        var title = TitleMatcher.Normalize(candidate.Media.Title);
        return (candidate.Media.MediaType,
            // An item without a usable title is never merged with another one
            title.Length > 0 ? title : "#" + candidate.Media.ExternalApiID,
            TitleMatcher.YearOf(candidate.Media.ReleaseDate),
            TitleMatcher.Normalize(candidate.Media.Creator));
    }

    private static int Bucket(double confidence) => (int)Math.Floor(Math.Clamp(confidence, 0, 1) / ConfidenceBucket + 1e-9);

    // Unknown dates last
    private static int CompareReleaseDates(string? a, string? b)
    {
        var hasA = TitleMatcher.YearOf(a) != null;
        var hasB = TitleMatcher.YearOf(b) != null;
        if (hasA != hasB) return hasA ? -1 : 1;
        return hasA ? string.CompareOrdinal(a, b) : 0;
    }
}
