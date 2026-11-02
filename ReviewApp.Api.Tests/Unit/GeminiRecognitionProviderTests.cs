using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ReviewApp.Api.Services.Recognition;
using ReviewApp.Api.Tests.Support;

namespace ReviewApp.Api.Tests.Unit;

// The Gemini client against a stubbed HTTP layer: what it sends, how it reads answers, retries and failures
public class GeminiRecognitionProviderTests
{
    private const string AnswerJson = """{"outcome":"recognised","summary":"We saw X.","works":[]}""";

    private static readonly byte[] Image = TestImages.Jpeg("gemini");

    private static GeminiRecognitionProvider Provider(StubHttpHandler handler, string thinkingLevel = "low") =>
        new(new HttpClient(handler),
            Options.Create(new RecognitionOptions { Model = "gemini-3.8-flash", ThinkingLevel = thinkingLevel, ProviderRetries = 1, ProviderTimeoutSeconds = 5 }),
            Options.Create(new GeminiOptions { ApiKey = "secret-key", BaseUrl = "https://gemini.test/v1beta" }),
            NullLogger<GeminiRecognitionProvider>.Instance);

    private static HttpResponseMessage Answer(string text, string finishReason = "STOP") => StubHttpHandler.Json(JsonSerializer.Serialize(new
    {
        candidates = new[] { new { content = new { parts = new[] { new { text } }, role = "model" }, finishReason } },
        usageMetadata = new { promptTokenCount = 1300, candidatesTokenCount = 400, thoughtsTokenCount = 200, totalTokenCount = 1900 }
    }));

    [Fact]
    public async Task Request_CarriesThePhotoInstructionsAndSettings_AndTheKeyOnlyInAHeader()
    {
        string? body = null;
        var handler = new StubHttpHandler(request =>
        {
            body = request.Content!.ReadAsStringAsync().Result;
            return Answer(AnswerJson);
        });

        var answer = await Provider(handler).RecognizeAsync(Image, "image/jpeg", CancellationToken.None);

        answer.ShouldBe(AnswerJson);
        var request = handler.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Post);
        request.RequestUri!.AbsoluteUri.ShouldBe("https://gemini.test/v1beta/models/gemini-3.8-flash:generateContent");
        request.Headers.GetValues("x-goog-api-key").Single().ShouldBe("secret-key");
        request.RequestUri.Query.ShouldNotContain("secret-key");

        var json = JsonDocument.Parse(body!).RootElement;
        json.GetProperty("system_instruction").GetProperty("parts")[0].GetProperty("text").GetString().ShouldContain("Never identify a real person");
        var parts = json.GetProperty("contents")[0].GetProperty("parts");
        parts[0].GetProperty("inline_data").GetProperty("mime_type").GetString().ShouldBe("image/jpeg");
        Convert.FromBase64String(parts[0].GetProperty("inline_data").GetProperty("data").GetString()!).ShouldBe(Image);
        parts[1].GetProperty("text").GetString().ShouldBe(RecognitionPrompt.UserMessage);
        var config = json.GetProperty("generationConfig");
        config.GetProperty("responseMimeType").GetString().ShouldBe("application/json");
        config.GetProperty("thinkingConfig").GetProperty("thinkingLevel").GetString().ShouldBe("low");
        config.GetProperty("responseSchema").GetProperty("properties").GetProperty("works").GetProperty("type").GetString().ShouldBe("ARRAY");
    }

    [Fact]
    public async Task EmptyThinkingLevel_LeavesTheModelDefault()
    {
        string? body = null;
        var handler = new StubHttpHandler(request =>
        {
            body = request.Content!.ReadAsStringAsync().Result;
            return Answer(AnswerJson);
        });

        await Provider(handler, thinkingLevel: "").RecognizeAsync(Image, "image/jpeg", CancellationToken.None);

        JsonDocument.Parse(body!).RootElement.GetProperty("generationConfig").TryGetProperty("thinkingConfig", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task ThoughtParts_AreNotPartOfTheAnswer()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""
            { "candidates": [ { "finishReason": "STOP", "content": { "parts": [
              { "text": "Let me think about the statue...", "thought": true },
              { "text": "{\"outcome\":" }, { "text": "\"recognised\"}" } ] } } ] }
            """));

        (await Provider(handler).RecognizeAsync(Image, "image/jpeg", CancellationToken.None)).ShouldBe("""{"outcome":"recognised"}""");
    }

    [Theory]
    [InlineData("MAX_TOKENS")]
    [InlineData("SAFETY")]
    [InlineData("PROHIBITED_CONTENT")]
    public async Task CutOffOrBlockedAnswers_AreUnusable(string finishReason)
    {
        var handler = new StubHttpHandler(_ => Answer("""{"outcome":"recogn""", finishReason));

        (await Provider(handler).RecognizeAsync(Image, "image/jpeg", CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task BlockedPhoto_IsUnusable()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{ "promptFeedback": { "blockReason": "SAFETY" } }"""));

        (await Provider(handler).RecognizeAsync(Image, "image/jpeg", CancellationToken.None)).ShouldBeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task TransientFailure_IsRetriedOnce(HttpStatusCode status)
    {
        var calls = 0;
        var handler = new StubHttpHandler(_ => ++calls == 1 ? new HttpResponseMessage(status) : Answer(AnswerJson));

        (await Provider(handler).RecognizeAsync(Image, "image/jpeg", CancellationToken.None)).ShouldBe(AnswerJson);
        calls.ShouldBe(2);
    }

    [Fact]
    public async Task PersistentRateLimiting_IsAQuotaError()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        var error = await Should.ThrowAsync<RecognitionProviderException>(() => Provider(handler).RecognizeAsync(Image, "image/jpeg", CancellationToken.None));

        error.Error.ShouldBe(RecognitionProviderError.QuotaExceeded);
        handler.Requests.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task ClientErrors_AreNotRetried(HttpStatusCode status)
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(status));

        var error = await Should.ThrowAsync<RecognitionProviderException>(() => Provider(handler).RecognizeAsync(Image, "image/jpeg", CancellationToken.None));

        error.Error.ShouldBe(RecognitionProviderError.Unavailable);
        handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task NetworkFailure_IsUnavailable()
    {
        var handler = new StubHttpHandler(_ => throw new HttpRequestException("no route"));

        var error = await Should.ThrowAsync<RecognitionProviderException>(() => Provider(handler).RecognizeAsync(Image, "image/jpeg", CancellationToken.None));

        error.Error.ShouldBe(RecognitionProviderError.Unavailable);
    }

    [Fact]
    public async Task UnreadableResponse_IsUnusable()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("<html>"));

        (await Provider(handler).RecognizeAsync(Image, "image/jpeg", CancellationToken.None)).ShouldBeNull();
    }
}
