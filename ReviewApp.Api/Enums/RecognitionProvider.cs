namespace ReviewApp.Api.Enums;

/// <summary>
/// The AI backend behind camera recognition, chosen with <c>Recognition:Provider</c>.
/// </summary>
public enum RecognitionProvider
{
    Gemini,
    /// <summary>Development only: a canned answer, no AI call and no API key.</summary>
    Fake
}
