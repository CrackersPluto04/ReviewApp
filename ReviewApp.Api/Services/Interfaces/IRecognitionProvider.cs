namespace ReviewApp.Api.Services.Interfaces;

// The AI vision model that describes a photo. Implementations: GeminiRecognitionProvider and the Development-only
// FakeRecognitionProvider; the test host uses its own fake.
public interface IRecognitionProvider
{
    // The answer as raw JSON in the shape of RecognitionPrompt, or null when the model gave no usable answer
    // (blocked, cut off). Throws RecognitionProviderException when the provider couldn't answer at all.
    Task<string?> RecognizeAsync(byte[] image, string mimeType, CancellationToken cancellationToken);
}
