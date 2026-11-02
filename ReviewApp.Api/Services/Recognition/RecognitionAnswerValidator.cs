using System.Globalization;
using System.Text;
using System.Text.Json;
using ReviewApp.Api.Enums;

namespace ReviewApp.Api.Services.Recognition;

// Turns the provider's raw JSON into a RecognitionAnswer the rest of the pipeline can trust. Never throws:
// anything unusable becomes "nothing recognised", unknown fields and values are ignored or dropped.
public static class RecognitionAnswerValidator
{
    public const int MaxWorks = 20;
    public const int MaxPeople = 5;
    public const int MaxFranchises = 5;
    public const int MaxTextLength = 120;
    public const int MaxSummaryLength = 200;
    private const double DefaultConfidence = 0.5;

    public static string DefaultSummary(RecognitionOutcome outcome) => outcome switch
    {
        RecognitionOutcome.NotMediaRelated => "We saw an object that isn't related to movies, series or music.",
        RecognitionOutcome.PersonNotIdentified => "We saw a person. People are not identified from their faces.",
        RecognitionOutcome.Recognised => "We recognised something related to movies, series or music.",
        _ => "We couldn't recognise anything in this photo."
    };

    public static RecognitionAnswer Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return RecognitionAnswer.Nothing();

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? Read(document.RootElement)
                : RecognitionAnswer.Nothing();
        }
        catch (JsonException)
        {
            return RecognitionAnswer.Nothing();
        }
    }

    private static RecognitionAnswer Read(JsonElement root)
    {
        var works = Array(root, "works").Select(ReadWork).OfType<RecognitionWork>().Take(MaxWorks).ToList();
        var people = Array(root, "people").Select(ReadPerson).OfType<RecognitionPerson>().Take(MaxPeople).ToList();
        var franchises = Array(root, "franchises")
            .Select(f => f.ValueKind == JsonValueKind.String ? Clean(f.GetString(), MaxTextLength) : null)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxFranchises)
            .ToList();

        var outcome = String(root, "outcome") switch
        {
            "recognised" => RecognitionOutcome.Recognised,
            "not_media_related" => RecognitionOutcome.NotMediaRelated,
            "person_not_identified" => RecognitionOutcome.PersonNotIdentified,
            "nothing_recognised" => RecognitionOutcome.NothingRecognised,
            // A missing or unknown outcome is judged by whether there is anything to work with
            _ => works.Count > 0 ? RecognitionOutcome.Recognised : RecognitionOutcome.NothingRecognised
        };

        var summary = Clean(String(root, "summary"), MaxSummaryLength) ?? DefaultSummary(outcome);

        // Nothing to suggest from: whatever the lists say is ignored
        if (outcome is RecognitionOutcome.NothingRecognised or RecognitionOutcome.NotMediaRelated)
            return RecognitionAnswer.Nothing(outcome, summary);

        // A person: never anyone's name (a printed name could still be in the works, through a costume or poster)
        if (outcome == RecognitionOutcome.PersonNotIdentified)
            people = [];

        return new RecognitionAnswer(outcome, summary, ReadSubject(root), franchises, people, works);
    }

    private static RecognitionSubject? ReadSubject(JsonElement root)
    {
        if (!root.TryGetProperty("subject", out var subject) || subject.ValueKind != JsonValueKind.Object)
            return null;

        var label = Clean(String(subject, "label"), MaxTextLength);
        if (label == null)
            return null;

        var kind = String(subject, "kind");
        return new RecognitionSubject(
            label,
            kind != null && RecognitionPrompt.SubjectKinds.Contains(kind) ? kind : "other",
            Confidence(subject));
    }

    private static RecognitionPerson? ReadPerson(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        var name = Clean(String(element, "name"), MaxTextLength);
        if (name == null)
            return null;

        var role = String(element, "role");
        return new RecognitionPerson(name, role != null && RecognitionPrompt.PersonRoles.Contains(role) ? role : "other", Confidence(element));
    }

    private static RecognitionWork? ReadWork(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        var title = Clean(String(element, "title"), MaxTextLength);
        MediaType? mediaType = String(element, "mediaType") switch
        {
            "movie" => MediaType.Movie,
            "series" => MediaType.Series,
            "music" => MediaType.Music,
            _ => null
        };
        RecognitionTier? relation = String(element, "relation") switch
        {
            "direct" => RecognitionTier.Direct,
            "universe" => RecognitionTier.Universe,
            "people" => RecognitionTier.People,
            "similar" => RecognitionTier.Similar,
            _ => null
        };
        if (title == null || mediaType == null || relation == null)
            return null;

        int? year = element.TryGetProperty("year", out var y) && y.ValueKind == JsonValueKind.Number && y.TryGetInt32(out var value)
                    && value is >= 1870 and <= 2100
            ? value
            : null;

        // Only music has a creator (the artist); it narrows the Spotify search
        var creator = mediaType == MediaType.Music ? Clean(String(element, "creator"), MaxTextLength) : null;

        return new RecognitionWork(title, mediaType.Value, year, creator, relation.Value, Confidence(element));
    }

    private static IEnumerable<JsonElement> Array(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array ? array.EnumerateArray() : [];

    private static string? String(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static double Confidence(JsonElement parent)
    {
        if (!parent.TryGetProperty("confidence", out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var confidence)
            || double.IsNaN(confidence))
            return DefaultConfidence;

        return Math.Clamp(confidence, 0, 1);
    }

    // Plain single-line text: control and format characters removed, whitespace collapsed, length capped. Null when empty.
    internal static string? Clean(string? text, int maxLength)
    {
        if (text == null)
            return null;

        var builder = new StringBuilder(Math.Min(text.Length, maxLength));
        var pendingSpace = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            var category = char.GetUnicodeCategory(c);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.OtherNotAssigned
                or UnicodeCategory.PrivateUse or UnicodeCategory.Surrogate)
                continue;

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }
            builder.Append(c);
        }

        var cleaned = builder.ToString();
        if (cleaned.Length > maxLength)
            cleaned = cleaned[..maxLength].TrimEnd();

        return cleaned.Length == 0 ? null : cleaned;
    }
}
