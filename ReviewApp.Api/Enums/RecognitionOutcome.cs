namespace ReviewApp.Api.Enums;

/// <summary>
/// What the AI made of the photo.
/// </summary>
public enum RecognitionOutcome
{
    Recognised,
    /// <summary>Blurry, dark, too generic, or the answer was unusable.</summary>
    NothingRecognised,
    /// <summary>A real object, but nothing to do with movies, series or music (a mug, a shoe).</summary>
    NotMediaRelated,
    /// <summary>A real person is the main subject. People are never identified from their faces.</summary>
    PersonNotIdentified
}
