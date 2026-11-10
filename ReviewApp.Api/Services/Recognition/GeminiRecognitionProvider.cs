using Microsoft.Extensions.Options;
using ReviewApp.Api.Services.Interfaces;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReviewApp.Api.Services.Recognition;

/// <summary>
/// Google Gemini through its REST API (<c>generateContent</c>): the photo inline, the instructions as the system
/// instruction, structured JSON output. Falls back to the next configured model when one is overloaded or out of
/// quota, and retries the last one. Logs status codes and token counts only, never the photo or the answer; the API
/// key goes in a header, so it never shows up in a logged URL.
/// </summary>
public class GeminiRecognitionProvider : IRecognitionProvider
{
    // Thinking tokens count against the output limit, so this leaves plenty of room for the answer itself
    private const int MaxOutputTokens = 4096;

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

    /// <summary>
    /// Tries the configured model, then the fallback models. A model that cannot answer right now (overloaded, out
    /// of quota, timed out, server error) is swapped for the next one at once: an overloaded model rarely recovers
    /// within seconds, and the user is waiting. Only the last model of the chain is retried, with a growing,
    /// jittered delay.
    /// </summary>
    /// <inheritdoc cref="IRecognitionProvider.RecognizeAsync"/>
    public async Task<string?> RecognizeAsync(byte[] image, string mimeType, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(BuildRequest(image, mimeType), JsonOptions);
        var models = Models();

        for (var index = 0; ; index++)
        {
            var model = models[index];
            var last = index == models.Count - 1;
            var attempts = last ? Math.Max(0, _options.ProviderRetries) + 1 : 1;

            for (var attempt = 1; ; attempt++)
            {
                var (answered, answer, failure) = await SendAsync(model, body, attempt, cancellationToken);
                if (answered)
                {
                    if (index > 0)
                        _logger.LogInformation("Gemini fallback model {Model} answered", model);
                    return answer;
                }

                if (attempt < attempts)
                {
                    await Task.Delay(RetryDelay(attempt), cancellationToken);
                    continue;
                }

                if (last)
                    throw failure!;

                _logger.LogWarning("Gemini model {Model} can't answer, falling back to {Next}", model, models[index + 1]);
                break;
            }
        }
    }

    // One call. A failure that may go away (rate limiting, server error, timeout, network) is returned; one that
    // won't (a 400 / 403 / 404: our request, our key or the model name) is thrown.
    private async Task<(bool Answered, string? Answer, RecognitionProviderException? Failure)> SendAsync(string model, string body, int attempt,
        CancellationToken cancellationToken)
    {
        using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attemptTimeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.ProviderTimeoutSeconds)));

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint(model))
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            };
            request.Headers.Add("x-goog-api-key", _gemini.ApiKey);

            using var response = await _httpClient.SendAsync(request, attemptTimeout.Token);
            if (response.IsSuccessStatusCode)
                return (true, ReadAnswer(await response.Content.ReadAsStringAsync(attemptTimeout.Token), model), null);

            _logger.LogWarning("Gemini {Model} answered {Status} (attempt {Attempt})", model, (int)response.StatusCode, attempt);
            var failure = response.StatusCode == HttpStatusCode.TooManyRequests
                ? new RecognitionProviderException(RecognitionProviderError.QuotaExceeded, "The recognition service is busy.")
                : new RecognitionProviderException(RecognitionProviderError.Unavailable, $"The recognition service answered {(int)response.StatusCode}.");

            if (response.StatusCode != HttpStatusCode.TooManyRequests && (int)response.StatusCode < 500)
                throw failure;
            return (false, null, failure);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Gemini {Model} timed out (attempt {Attempt})", model, attempt);
            return (false, null, new RecognitionProviderException(RecognitionProviderError.Unavailable, "The recognition service timed out.", ex));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("Gemini {Model} request failed: {Error} (attempt {Attempt})", model, ex.HttpRequestError, attempt);
            return (false, null, new RecognitionProviderException(RecognitionProviderError.Unavailable, "The recognition service is unreachable.", ex));
        }
    }

    private List<string> Models() =>
        new[] { _options.Model }
            .Concat(_options.FallbackModels ?? [])
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    // 1 s, 2 s, 4 s ... plus up to a quarter second, so parallel retries don't arrive together
    private static TimeSpan RetryDelay(int attempt) =>
        TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)) + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 250));

    private Uri Endpoint(string model) =>
        new(new Uri(_gemini.BaseUrl.EndsWith('/') ? _gemini.BaseUrl : _gemini.BaseUrl + "/"),
            $"models/{Uri.EscapeDataString(model)}:generateContent");

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
    private string? ReadAnswer(string json, string model)
    {
        try
        {
            var response = JsonSerializer.Deserialize<GenerateContentResponse>(json);
            LogUsage(response?.UsageMetadata, model);

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

    private void LogUsage(UsageMetadata? usage, string model)
    {
        if (usage != null)
            _logger.LogInformation("Gemini tokens ({Model}): prompt {Prompt}, output {Output}, thinking {Thinking}, total {Total}", model,
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
