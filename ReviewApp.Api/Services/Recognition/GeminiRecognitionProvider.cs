using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Services.Recognition;

// Google Gemini through its REST API (generateContent): the photo inline, the instructions as the system instruction,
// structured JSON output. One retry on rate limiting, server errors and timeouts. Logs status codes and token counts
// only, never the photo or the answer; the API key goes in a header, so it never shows up in a logged URL.
public class GeminiRecognitionProvider : IRecognitionProvider
{
    // Thinking tokens count against the output limit, so this leaves plenty of room for the answer itself
    private const int MaxOutputTokens = 4096;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly RecognitionOptions _options;
    private readonly GeminiOptions _gemini;
    private readonly ILogger<GeminiRecognitionProvider> _logger;

    public GeminiRecognitionProvider(HttpClient httpClient, IOptions<RecognitionOptions> options, IOptions<GeminiOptions> gemini,
        ILogger<GeminiRecognitionProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _gemini = gemini.Value;
        _logger = logger;
        // Our own per-attempt timeout applies instead
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
    }

    public async Task<string?> RecognizeAsync(byte[] image, string mimeType, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(BuildRequest(image, mimeType), JsonOptions);
        var attempts = Math.Max(0, _options.ProviderRetries) + 1;

        for (var attempt = 1; ; attempt++)
        {
            using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptTimeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.ProviderTimeoutSeconds)));

            RecognitionProviderException failure;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint())
                {
                    Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
                };
                request.Headers.Add("x-goog-api-key", _gemini.ApiKey);

                using var response = await _httpClient.SendAsync(request, attemptTimeout.Token);
                if (response.IsSuccessStatusCode)
                    return ReadAnswer(await response.Content.ReadAsStringAsync(attemptTimeout.Token));

                _logger.LogWarning("Gemini answered {Status} (attempt {Attempt})", (int)response.StatusCode, attempt);
                failure = response.StatusCode == HttpStatusCode.TooManyRequests
                    ? new RecognitionProviderException(RecognitionProviderError.QuotaExceeded, "The recognition service is busy.")
                    : new RecognitionProviderException(RecognitionProviderError.Unavailable, $"The recognition service answered {(int)response.StatusCode}.");

                // Only rate limiting and server errors can go away by retrying (a 400 / 403 / 404 is our request or our key)
                if (response.StatusCode != HttpStatusCode.TooManyRequests && (int)response.StatusCode < 500)
                    throw failure;
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Gemini timed out (attempt {Attempt})", attempt);
                failure = new RecognitionProviderException(RecognitionProviderError.Unavailable, "The recognition service timed out.", ex);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning("Gemini request failed: {Error} (attempt {Attempt})", ex.HttpRequestError, attempt);
                failure = new RecognitionProviderException(RecognitionProviderError.Unavailable, "The recognition service is unreachable.", ex);
            }

            if (attempt >= attempts)
                throw failure;

            await Task.Delay(RetryDelay, cancellationToken);
        }
    }

    private Uri Endpoint() =>
        new(new Uri(_gemini.BaseUrl.EndsWith('/') ? _gemini.BaseUrl : _gemini.BaseUrl + "/"),
            $"models/{Uri.EscapeDataString(_options.Model)}:generateContent");

    private object BuildRequest(byte[] image, string mimeType) => new
    {
        system_instruction = new { parts = new[] { new { text = RecognitionPrompt.SystemInstruction(_options.SimilarFillThreshold) } } },
        contents = new[]
        {
            new
            {
                role = "user",
                parts = new object[]
                {
                    new { inline_data = new { mime_type = mimeType, data = Convert.ToBase64String(image) } },
                    new { text = RecognitionPrompt.UserMessage }
                }
            }
        },
        generationConfig = new
        {
            responseMimeType = "application/json",
            responseSchema = RecognitionPrompt.ResponseSchema,
            maxOutputTokens = MaxOutputTokens,
            thinkingConfig = string.IsNullOrWhiteSpace(_options.ThinkingLevel) ? null : new { thinkingLevel = _options.ThinkingLevel }
        }
    };

    // The text of the first candidate, or null when the model was blocked or cut off
    private string? ReadAnswer(string json)
    {
        try
        {
            var response = JsonSerializer.Deserialize<GenerateContentResponse>(json);
            LogUsage(response?.UsageMetadata);

            if (!string.IsNullOrEmpty(response?.PromptFeedback?.BlockReason))
            {
                _logger.LogInformation("Gemini blocked the photo: {Reason}", response.PromptFeedback.BlockReason);
                return null;
            }

            var candidate = response?.Candidates?.FirstOrDefault();
            if (candidate?.FinishReason is not (null or "STOP"))
            {
                _logger.LogInformation("Gemini stopped early: {Reason}", candidate?.FinishReason);
                return null;
            }

            var text = string.Concat(candidate?.Content?.Parts?.Where(p => p.Thought != true).Select(p => p.Text) ?? []);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (JsonException)
        {
            _logger.LogWarning("Gemini sent an unreadable response");
            return null;
        }
    }

    private void LogUsage(UsageMetadata? usage)
    {
        if (usage != null)
            _logger.LogInformation("Gemini tokens: prompt {Prompt}, output {Output}, thinking {Thinking}, total {Total}",
                usage.PromptTokenCount, usage.CandidatesTokenCount, usage.ThoughtsTokenCount, usage.TotalTokenCount);
    }

    private sealed class GenerateContentResponse
    {
        [JsonPropertyName("candidates")] public List<Candidate>? Candidates { get; set; }
        [JsonPropertyName("promptFeedback")] public PromptFeedback? PromptFeedback { get; set; }
        [JsonPropertyName("usageMetadata")] public UsageMetadata? UsageMetadata { get; set; }
    }

    private sealed class Candidate
    {
        [JsonPropertyName("content")] public Content? Content { get; set; }
        [JsonPropertyName("finishReason")] public string? FinishReason { get; set; }
    }

    private sealed class Content
    {
        [JsonPropertyName("parts")] public List<Part>? Parts { get; set; }
    }

    private sealed class Part
    {
        [JsonPropertyName("text")] public string? Text { get; set; }
        [JsonPropertyName("thought")] public bool? Thought { get; set; }
    }

    private sealed class PromptFeedback
    {
        [JsonPropertyName("blockReason")] public string? BlockReason { get; set; }
    }

    private sealed class UsageMetadata
    {
        [JsonPropertyName("promptTokenCount")] public int PromptTokenCount { get; set; }
        [JsonPropertyName("candidatesTokenCount")] public int CandidatesTokenCount { get; set; }
        [JsonPropertyName("thoughtsTokenCount")] public int ThoughtsTokenCount { get; set; }
        [JsonPropertyName("totalTokenCount")] public int TotalTokenCount { get; set; }
    }
}
