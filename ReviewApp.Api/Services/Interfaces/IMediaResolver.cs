using ReviewApp.Api.Services.Recognition;

namespace ReviewApp.Api.Services.Interfaces;

// Turns the AI's answer into real media items through TMDb / Spotify. Never throws for a media source outage:
// it reports how many lookups failed, so the caller can tell "nothing matched" from "couldn't look".
public interface IMediaResolver
{
    Task<ResolveResult> ResolveAsync(RecognitionAnswer answer, CancellationToken cancellationToken);
}

public record ResolveResult(IReadOnlyList<RankCandidate> Candidates, int Lookups, int Failures);
