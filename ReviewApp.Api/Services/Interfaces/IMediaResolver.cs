using ReviewApp.Api.Services.Recognition;

namespace ReviewApp.Api.Services.Interfaces;

/// <summary>
/// Turns the AI's answer into real media items through TMDb / Spotify. Never throws for a media source outage:
/// it reports how many lookups failed, so the caller can tell "nothing matched" from "could not look".
/// </summary>
public interface IMediaResolver
{
    /// <summary>Looks the answer's titles, franchises and people up and collects candidates for ranking.</summary>
    Task<ResolveResult> ResolveAsync(RecognitionAnswer answer, CancellationToken cancellationToken);
}

/// <summary>
/// The outcome of resolving an answer.
/// </summary>
/// <param name="Candidates">The media found, with how each relates to the photo.</param>
/// <param name="Lookups">How many source lookups were made.</param>
/// <param name="Failures">How many of them failed. All lookups failing with no candidates means a source outage, not "nothing matched".</param>
public record ResolveResult(IReadOnlyList<RankCandidate> Candidates, int Lookups, int Failures);
