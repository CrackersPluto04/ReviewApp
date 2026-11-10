using ReviewApp.Api.Enums;

namespace ReviewApp.Api.Services.Recognition;

/// <summary>
/// The AI's description of the photo after validation (<see cref="RecognitionAnswerValidator"/>): every string is
/// cleaned and length-limited, every confidence is between 0 and 1, and the lists are capped. Nothing in it can
/// change what the app does: titles and names are only ever used as search queries.
/// </summary>
/// <param name="Outcome">What the AI concluded about the photo.</param>
/// <param name="Summary">A one-sentence description for the user.</param>
/// <param name="Subject">What the photo shows, if the AI named it.</param>
/// <param name="Franchises">Franchise names, searched for their TMDb collections.</param>
/// <param name="People">Actors or directors connected to the subject.</param>
/// <param name="Works">Specific movies, series or music the subject relates to.</param>
public record RecognitionAnswer(
    RecognitionOutcome Outcome,
    string Summary,
    RecognitionSubject? Subject,
    IReadOnlyList<string> Franchises,
    IReadOnlyList<RecognitionPerson> People,
    IReadOnlyList<RecognitionWork> Works)
{
    /// <summary>An answer with nothing to suggest from, used for unusable or empty provider output.</summary>
    public static RecognitionAnswer Nothing(RecognitionOutcome outcome = RecognitionOutcome.NothingRecognised, string? summary = null) =>
        new(outcome, summary ?? RecognitionAnswerValidator.DefaultSummary(outcome), null, [], [], []);
}

/// <summary>What the photo shows (a figure, poster, album cover and so on).</summary>
/// <param name="Label">A short description.</param>
/// <param name="Kind">One of <c>RecognitionPrompt.SubjectKinds</c>; unknown kinds are stored as <c>other</c>.</param>
/// <param name="Confidence">The AI's confidence, 0 to 1.</param>
public record RecognitionSubject(string Label, string Kind, double Confidence);

/// <summary>An actor, director or similar person connected to the subject. Never identified from a face.</summary>
/// <param name="Name">The person's name.</param>
/// <param name="Role">One of <c>RecognitionPrompt.PersonRoles</c>; unknown roles are stored as <c>other</c>.</param>
/// <param name="Confidence">The AI's confidence, 0 to 1.</param>
public record RecognitionPerson(string Name, string Role, double Confidence);

/// <summary>A movie, series or music the subject relates to.</summary>
/// <param name="Title">The title to search for.</param>
/// <param name="MediaType">Movie, series or music.</param>
/// <param name="Year">The release year when the AI gave a plausible one (1870 to 2100).</param>
/// <param name="Creator">The artist, for music only; it narrows the Spotify search.</param>
/// <param name="Relation">How closely it relates to the photo, which decides its tier.</param>
/// <param name="Confidence">The AI's confidence, 0 to 1.</param>
public record RecognitionWork(string Title, MediaType MediaType, int? Year, string? Creator, RecognitionTier Relation, double Confidence);

/// <summary>Why the provider could not answer.</summary>
public enum RecognitionProviderError
{
    /// <summary>Down, timed out or misconfigured.</summary>
    Unavailable,
    /// <summary>The provider's own rate limit or quota.</summary>
    QuotaExceeded
}

/// <summary>
/// The provider could not answer at all. Not counted toward the daily allowance.
/// </summary>
public class RecognitionProviderException : Exception
{
    public RecognitionProviderException(RecognitionProviderError error, string message, Exception? inner = null) : base(message, inner)
    {
        Error = error;
    }

    public RecognitionProviderError Error { get; }
}

/// <summary>
/// TMDb / Spotify failed (as opposed to "found nothing"), so a caller can tell an outage from an unknown title.
/// </summary>
public class MediaSourceUnavailableException : Exception
{
    public MediaSourceUnavailableException(string source, Exception? inner = null) : base($"{source} is unavailable.", inner) { }
}
