namespace ReviewApp.Api.Services.Interfaces;

/// <summary>
/// The AI vision model that describes a photo. Implementations: <c>GeminiRecognitionProvider</c> and the
/// Development-only <c>FakeRecognitionProvider</c>; the test host uses its own fake.
/// </summary>
public interface IRecognitionProvider
{
    /// <summary>Asks the model what the photo shows.</summary>
    /// <param name="image">The photo bytes; kept in memory only.</param>
    /// <param name="mimeType">The image's MIME type, e.g. <c>image/jpeg</c>.</param>
    /// <param name="cancellationToken">Cancelled when the user cancels or disconnects, which abandons the provider call.</param>
    /// <returns>The answer as raw JSON in the shape of <c>RecognitionPrompt</c>, or null when the model gave no usable answer (blocked, cut off).</returns>
    /// <exception cref="Recognition.RecognitionProviderException">The provider could not answer at all.</exception>
    Task<string?> RecognizeAsync(byte[] image, string mimeType, CancellationToken cancellationToken);
}
