using System.Globalization;
using System.Text;

namespace ReviewApp.Api.Services.Recognition;

// Decides whether a search hit is the work the AI named. Precision over recall: an exact title (after
// normalisation) with a fitting year counts; a longer / shorter form of the title (TitleContains) only with more
// evidence; any other near miss is dropped rather than shown as a guess.
public static class TitleMatcher
{
    // Lower case, no diacritics, "&" = "and", punctuation as spaces, collapsed whitespace, no leading "the"
    public static string Normalize(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return string.Empty;

        var decomposed = title.Replace("&", " and ").Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var pendingSpace = false;
        foreach (var c in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;

            // Apostrophes join words ("Schindler's" = "Schindlers"), other punctuation separates them
            if (c is '\'' or '’')
                continue;

            if (char.IsLetterOrDigit(c))
            {
                if (pendingSpace && builder.Length > 0)
                    builder.Append(' ');
                pendingSpace = false;
                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                pendingSpace = true;
            }
        }

        var normalized = builder.ToString();
        return normalized.StartsWith("the ", StringComparison.Ordinal) ? normalized[4..] : normalized;
    }

    public static bool TitleMatches(string? wanted, string? candidate)
    {
        var normalizedWanted = Normalize(wanted);
        return normalizedWanted.Length > 0 && normalizedWanted == Normalize(candidate);
    }

    // The same work under a longer or shorter name: the words of one title appear, in order, in the other
    // ("Star Wars: Episode IV - A New Hope" = TMDb's "Star Wars", "Minecraft" = "A Minecraft Movie"). Much weaker
    // than TitleMatches, so the resolver only accepts it with more evidence (a matching year, a top search result).
    public static bool TitleContains(string? wanted, string? candidate)
    {
        var a = Words(wanted);
        var b = Words(candidate);
        if (a.Length == 0 || b.Length == 0 || a.Length == b.Length)
            return false;

        return a.Length < b.Length ? IsSubsequence(a, b) : IsSubsequence(b, a);
    }

    private static string[] Words(string? title) => Normalize(title).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static bool IsSubsequence(string[] shorter, string[] longer)
    {
        var next = 0;
        foreach (var word in longer)
        {
            if (next < shorter.Length && word == shorter[next])
                next++;
        }
        return next == shorter.Length;
    }

    // Release dates are "yyyy-MM-dd" (TMDb) or "yyyy" / "yyyy-MM" (Spotify)
    public static int? YearOf(string? releaseDate) =>
        releaseDate is { Length: >= 4 } && int.TryParse(releaseDate.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            ? year
            : null;

    // One year of tolerance (festival vs. theatrical release, AI rounding); an unknown year on either side is no evidence against
    public static bool YearMatches(int? wanted, string? releaseDate)
    {
        var year = YearOf(releaseDate);
        return wanted == null || year == null || Math.Abs(year.Value - wanted.Value) <= 1;
    }
}
