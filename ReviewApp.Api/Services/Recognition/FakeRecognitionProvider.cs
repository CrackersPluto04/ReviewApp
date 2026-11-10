using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Services.Recognition;

/// <summary>
/// Development-only provider (<c>Recognition:Provider = Fake</c>): every photo is "an Iron Man statue", without an
/// AI call or an API key, so the whole flow (TMDb / Spotify resolution, ranking, paging, the clients) can be tried
/// for free. <c>Program.cs</c> refuses to start with it outside Development.
/// </summary>
public class FakeRecognitionProvider : IRecognitionProvider
{
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(1.5);

    /// <summary>The canned answer, in the shape of <see cref="RecognitionPrompt"/>. Covers all four tiers so ranking and paging can be exercised.</summary>
    public const string Answer = """
        {
          "outcome": "recognised",
          "summary": "We saw an Iron Man statue (Fake provider in Development, every photo gives this answer).",
          "subject": { "label": "Iron Man statue", "kind": "statue", "confidence": 0.95 },
          "franchises": ["Marvel Cinematic Universe"],
          "people": [ { "name": "Robert Downey Jr.", "role": "actor", "confidence": 0.9 } ],
          "works": [
            { "title": "Iron Man", "mediaType": "movie", "year": 2008, "creator": null, "relation": "direct", "confidence": 0.97 },
            { "title": "Iron Man 2", "mediaType": "movie", "year": 2010, "creator": null, "relation": "direct", "confidence": 0.95 },
            { "title": "Iron Man 3", "mediaType": "movie", "year": 2013, "creator": null, "relation": "direct", "confidence": 0.95 },
            { "title": "The Avengers", "mediaType": "movie", "year": 2012, "creator": null, "relation": "universe", "confidence": 0.9 },
            { "title": "Avengers: Age of Ultron", "mediaType": "movie", "year": 2015, "creator": null, "relation": "universe", "confidence": 0.88 },
            { "title": "Captain America: Civil War", "mediaType": "movie", "year": 2016, "creator": null, "relation": "universe", "confidence": 0.85 },
            { "title": "Spider-Man: Homecoming", "mediaType": "movie", "year": 2017, "creator": null, "relation": "universe", "confidence": 0.8 },
            { "title": "Avengers: Infinity War", "mediaType": "movie", "year": 2018, "creator": null, "relation": "universe", "confidence": 0.85 },
            { "title": "Avengers: Endgame", "mediaType": "movie", "year": 2019, "creator": null, "relation": "universe", "confidence": 0.85 },
            { "title": "WandaVision", "mediaType": "series", "year": 2021, "creator": null, "relation": "universe", "confidence": 0.7 },
            { "title": "Back in Black", "mediaType": "music", "year": 1980, "creator": "AC/DC", "relation": "universe", "confidence": 0.6 },
            { "title": "Sherlock Holmes", "mediaType": "movie", "year": 2009, "creator": null, "relation": "people", "confidence": 0.75 },
            { "title": "Oppenheimer", "mediaType": "movie", "year": 2023, "creator": null, "relation": "people", "confidence": 0.75 },
            { "title": "Tropic Thunder", "mediaType": "movie", "year": 2008, "creator": null, "relation": "people", "confidence": 0.7 }
          ]
        }
        """;

    public async Task<string?> RecognizeAsync(byte[] image, string mimeType, CancellationToken cancellationToken)
    {
        // A little wait, so the clients' progress state can be seen
        await Task.Delay(Delay, cancellationToken);
        return Answer;
    }
}
