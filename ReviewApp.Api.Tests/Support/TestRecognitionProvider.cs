using System.Collections.Concurrent;
using System.Text.Json;
using ReviewApp.Api.Services.Interfaces;
using ReviewApp.Api.Services.Recognition;

namespace ReviewApp.Api.Tests.Support;

// The AI vision provider of the test host. Answers are scripted per photo: TestImages puts a tag into the image,
// and the fake answers by that tag, so tests running in parallel never see each other's answers.
// An unscripted tag gets DefaultAnswer: twelve movies named after the tag (enough for "Show more").
public class TestRecognitionProvider : IRecognitionProvider
{
    private readonly ConcurrentDictionary<string, Func<CancellationToken, Task<string?>>> _scripts = new();
    private readonly ConcurrentDictionary<string, int> _calls = new();

    public void Answer(string tag, object answer) => AnswerRaw(tag, JsonSerializer.Serialize(answer));

    public void AnswerRaw(string tag, string? raw) => _scripts[tag] = _ => Task.FromResult(raw);

    public void Fail(string tag, RecognitionProviderError error = RecognitionProviderError.Unavailable) =>
        _scripts[tag] = _ => throw new RecognitionProviderException(error, "Scripted failure.");

    // Answers only when `release` completes, to keep a recognition running
    public void Hold(string tag, Task release) => _scripts[tag] = async _ =>
    {
        await release;
        return JsonSerializer.Serialize(DefaultAnswer(tag));
    };

    public int Calls(string tag) => _calls.TryGetValue(tag, out var calls) ? calls : 0;

    public Task<string?> RecognizeAsync(byte[] image, string mimeType, CancellationToken cancellationToken)
    {
        var tag = TestImages.TagOf(image) ?? string.Empty;
        _calls.AddOrUpdate(tag, 1, (_, calls) => calls + 1);
        return _scripts.TryGetValue(tag, out var script)
            ? script(cancellationToken)
            : Task.FromResult<string?>(JsonSerializer.Serialize(DefaultAnswer(tag)));
    }

    public static string DirectTitle(string tag, int index) => $"{tag} Origins {index}";
    public static string UniverseTitle(string tag, int index) => $"{tag} Universe {index}";

    // 3 direct and 9 universe movies, every confidence above the minimum
    public static object DefaultAnswer(string tag) => new
    {
        outcome = "recognised",
        summary = $"We saw {tag}.",
        subject = new { label = $"{tag} figure", kind = "figure", confidence = 0.9 },
        franchises = new[] { $"{tag} franchise" },
        people = Array.Empty<object>(),
        works = Enumerable.Range(1, 3).Select(i => Work(DirectTitle(tag, i), "direct", 0.95))
            .Concat(Enumerable.Range(1, 9).Select(i => Work(UniverseTitle(tag, i), "universe", 0.8)))
            .ToArray()
    };

    public static object Work(string title, string relation, double confidence, string mediaType = "movie", int? year = null, string? creator = null) =>
        new { title, mediaType, year, creator, relation, confidence };
}

// Photos for the tests: headers only (the pipeline never decodes pixels), plus a tag the fake provider answers by
public static class TestImages
{
    private const string TagMarker = "tag:";

    public static byte[] Jpeg(string tag = "", int width = 800, int height = 600)
    {
        var comment = System.Text.Encoding.ASCII.GetBytes($"{TagMarker}{tag};");
        var bytes = new List<byte> { 0xFF, 0xD8 };
        // A comment segment before the frame header, like real files have APP segments there
        bytes.AddRange([0xFF, 0xFE, (byte)((comment.Length + 2) >> 8), (byte)((comment.Length + 2) & 0xFF)]);
        bytes.AddRange(comment);
        // Baseline frame header: length 17, precision 8, height, width, 3 components
        bytes.AddRange([0xFF, 0xC0, 0x00, 0x11, 0x08, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width,
            0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01]);
        bytes.AddRange([0xFF, 0xD9]);
        return [.. bytes];
    }

    public static byte[] Png(int width = 640, int height = 480)
    {
        var bytes = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D };
        bytes.AddRange("IHDR"u8.ToArray());
        bytes.AddRange(BigEndian(width));
        bytes.AddRange(BigEndian(height));
        bytes.AddRange([0x08, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]);
        return [.. bytes];
    }

    // An iPhone photo's beginning (ISO base media "ftyp" box, brand heic)
    public static byte[] Heic() => [0x00, 0x00, 0x00, 0x18, .. "ftypheic"u8.ToArray(), 0x00, 0x00, 0x00, 0x00, .. "mif1heic"u8.ToArray()];

    public static string? TagOf(byte[] image)
    {
        var text = System.Text.Encoding.ASCII.GetString(image);
        var start = text.IndexOf(TagMarker, StringComparison.Ordinal);
        if (start < 0)
            return null;
        start += TagMarker.Length;
        var end = text.IndexOf(';', start);
        return end < 0 ? null : text[start..end];
    }

    private static byte[] BigEndian(int value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
}
