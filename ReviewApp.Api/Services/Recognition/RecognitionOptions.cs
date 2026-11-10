using ReviewApp.Api.Enums;

namespace ReviewApp.Api.Services.Recognition;

/// <summary>
/// Bound from the <c>Recognition</c> configuration section. Every limit, page size and weight of the camera
/// recognition feature lives here, so tuning it never means hunting through the code.
/// </summary>
public class RecognitionOptions
{
    public const string SectionName = "Recognition";

    /// <summary>
    /// Ceiling for the upload request (photo plus multipart overhead), enforced by the server before the action
    /// runs. <see cref="MaxImageBytes"/> is the real limit and must stay below it.
    /// </summary>
    public const long MaxRequestBytes = 16 * 1024 * 1024;

    /// <summary>Off: the endpoints answer 503 and the allowance endpoint reports <c>enabled = false</c>, so the clients hide the entry.</summary>
    public bool Enabled { get; set; } = true;

    public RecognitionProvider Provider { get; set; } = RecognitionProvider.Gemini;
    public string Model { get; set; } = "gemini-3.8-flash";
    /// <summary>
    /// Tried in order when the model cannot answer right now (overloaded, out of quota, timed out, server error),
    /// with the same request and thinking level, so they must be models that accept both (the Gemini 3 family).
    /// </summary>
    public string[] FallbackModels { get; set; } = [];
    /// <summary>Empty means the model's default.</summary>
    public string ThinkingLevel { get; set; } = "low";

    public int DailyLimitPerUser { get; set; } = 20;

    /// <summary>The clients send a downscaled JPEG (about 1280 px), so the two image limits only stop abuse.</summary>
    public int MaxImageBytes { get; set; } = 5 * 1024 * 1024;
    public long MaxImagePixels { get; set; } = 4096L * 4096L;

    /// <summary>Items on the first response, and on each "Show more".</summary>
    public int FirstPageSize { get; set; } = 10;
    public int PageSize { get; set; } = 5;
    /// <summary>The most items one recognition keeps.</summary>
    public int TotalCap { get; set; } = 60;
    /// <summary>How long "Show more" works after a recognition. The list is kept in memory and lost on restart.</summary>
    public int ResultTtlMinutes { get; set; } = 30;

    /// <summary>Works the AI is less sure about than this are dropped.</summary>
    public double MinConfidence { get; set; } = 0.4;
    /// <summary>The Similar tier is only used when the closer tiers hold fewer items than this.</summary>
    public int SimilarFillThreshold { get; set; } = 10;

    /// <summary>One call to one model.</summary>
    public int ProviderTimeoutSeconds { get; set; } = 30;
    /// <summary>Extra attempts of the last model in the chain; the others are swapped for the next model at once.</summary>
    public int ProviderRetries { get; set; } = 1;
    /// <summary>
    /// Every attempt of one recognition together, fallbacks included. Together with
    /// <see cref="ResolveDeadlineSeconds"/> it must stay below the clients' timeouts (the Android app waits 90 s).
    /// </summary>
    public int ProviderBudgetSeconds { get; set; } = 60;

    /// <summary>TMDb / Spotify lookups of one recognition: total time, number of calls, parallel calls.</summary>
    public int ResolveDeadlineSeconds { get; set; } = 15;
    public int MaxLookups { get; set; } = 30;
    public int MaxParallelLookups { get; set; } = 4;

    /// <summary>Expansion beyond the works the AI named: the TMDb collection of the top direct movies.</summary>
    public bool ExpandCollections { get; set; } = true;
    /// <summary>Expansion beyond the works the AI named: the best-known work of the connected actors and directors.</summary>
    public bool ExpandPeople { get; set; } = true;
}

/// <summary>
/// Bound from the <c>Gemini</c> configuration section. The key only goes into the gitignored
/// <c>appsettings.Development.json</c>.
/// </summary>
public class GeminiOptions
{
    public const string SectionName = "Gemini";

    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta/";
}
