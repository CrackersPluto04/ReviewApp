namespace ReviewApp.Api.Enums;

// What the AI made of the photo
public enum RecognitionOutcome
{
    Recognised,
    NothingRecognised,   // blurry, dark, too generic, or the answer was unusable
    NotMediaRelated,     // a real object, but nothing to do with movies, series or music (a mug, a shoe)
    PersonNotIdentified  // a real person is the main subject; people are never identified from their faces
}
