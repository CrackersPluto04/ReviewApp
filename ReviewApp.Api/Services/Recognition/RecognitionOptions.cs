using ReviewApp.Api.Enums;

namespace ReviewApp.Api.Services.Recognition;

// Bound from the "Recognition" configuration section. Every limit, page size and weight of the camera
// recognition feature lives here, so tuning it never means hunting through the code.
public class RecognitionOptions
{
    public const string SectionName = "Recognition";

    // Ceiling for the upload request (photo + multipart overhead), enforced by the server before the action runs.
    // MaxImageBytes is the real limit and must stay below it.
    public const long MaxRequestBytes = 16 * 1024 * 1024;

    // Off: the endpoints answer 503 and the allowance endpoint reports enabled = false, so the clients hide the entry
    public bool Enabled { get; set; } = true;

    public RecognitionProvider Provider { get; set; } = RecognitionProvider.Gemini;
    public string Model { get; set; } = "gemini-3.8-flash";
    // Empty = the model's default
    public string ThinkingLevel { get; set; } = "low";

    public int DailyLimitPerUser { get; set; } = 20;

    // The clients send a downscaled JPEG (about 1280 px), so these only stop abuse
    public int MaxImageBytes { get; set; } = 5 * 1024 * 1024;
    public long MaxImagePixels { get; set; } = 4096L * 4096L;

    public int FirstPageSize { get; set; } = 10;
    public int PageSize { get; set; } = 5;
    public int TotalCap { get; set; } = 60;
    // How long "Show more" works after a recognition (kept in memory, lost on restart)
    public int ResultTtlMinutes { get; set; } = 30;

    // Works the AI is less sure about than this are dropped
    public double MinConfidence { get; set; } = 0.4;
    // The Similar tier is only used when the closer tiers hold fewer items than this
    public int SimilarFillThreshold { get; set; } = 10;

    public int ProviderTimeoutSeconds { get; set; } = 30;
    public int ProviderRetries { get; set; } = 1;

    // TMDb / Spotify lookups of one recognition: total time, number of calls, parallel calls
    public int ResolveDeadlineSeconds { get; set; } = 15;
    public int MaxLookups { get; set; } = 30;
    public int MaxParallelLookups { get; set; } = 4;

    // Expansion beyond the works the AI named: the TMDb collection of the top direct movies, the best-known
    // work of the connected actors / directors
    public bool ExpandCollections { get; set; } = true;
    public bool ExpandPeople { get; set; } = true;
}

// Bound from the "Gemini" configuration section. The key only goes into the gitignored appsettings.Development.json.
public class GeminiOptions
{
    public const string SectionName = "Gemini";

    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta/";
}
