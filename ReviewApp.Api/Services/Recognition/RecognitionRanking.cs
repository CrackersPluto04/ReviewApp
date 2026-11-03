using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;

namespace ReviewApp.Api.Services.Recognition;

// A real media item found for the recognition, before ranking
public record RankCandidate(MediaDto Media, RecognitionTier Tier, double Confidence, double Popularity);

// A ranked suggestion; the list position is its rank
public record RankedItem(MediaDto Media, RecognitionTier Tier);

public record RankedPage(IReadOnlyList<RankedItem> Items, int NextOffset, bool HasMore, int Total);

// Orders the suggestions of one recognition: closest tier first, then the AI's confidence, then popularity.
// Deterministic, so paging over the stored list never repeats or skips an item.
public static class RecognitionRanking
{
    // Confidences this close count as equal, so popularity decides between them (0.82 vs 0.84 means nothing)
    private const double ConfidenceBucket = 0.05;

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
