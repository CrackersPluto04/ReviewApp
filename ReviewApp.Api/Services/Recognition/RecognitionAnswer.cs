using ReviewApp.Api.Enums;

namespace ReviewApp.Api.Services.Recognition;

// The AI's description of the photo after validation (RecognitionAnswerValidator): every string is cleaned and
// length-limited, every confidence is between 0 and 1, the lists are capped. Nothing in it can change what the
// app does; titles and names are only ever used as search queries.
public record RecognitionAnswer(
    RecognitionOutcome Outcome,
    string Summary,
    RecognitionSubject? Subject,
    IReadOnlyList<string> Franchises,
    IReadOnlyList<RecognitionPerson> People,
    IReadOnlyList<RecognitionWork> Works)
{
    public static RecognitionAnswer Nothing(RecognitionOutcome outcome = RecognitionOutcome.NothingRecognised, string? summary = null) =>
        new(outcome, summary ?? RecognitionAnswerValidator.DefaultSummary(outcome), null, [], [], []);
}

public record RecognitionSubject(string Label, string Kind, double Confidence);

public record RecognitionPerson(string Name, string Role, double Confidence);

public record RecognitionWork(string Title, MediaType MediaType, int? Year, string? Creator, RecognitionTier Relation, double Confidence);

public enum RecognitionProviderError
{
    Unavailable,  // down, timed out, misconfigured
    QuotaExceeded // the provider's own rate limit / quota
}

// The provider could not answer at all. Not counted toward the daily allowance.
public class RecognitionProviderException : Exception
{
    public RecognitionProviderException(RecognitionProviderError error, string message, Exception? inner = null) : base(message, inner)
    {
        Error = error;
    }

    public RecognitionProviderError Error { get; }
}

// TMDb / Spotify failed (not "found nothing"), so a recognition can tell an outage from an unknown title
public class MediaSourceUnavailableException : Exception
{
    public MediaSourceUnavailableException(string source, Exception? inner = null) : base($"{source} is unavailable.", inner) { }
}
