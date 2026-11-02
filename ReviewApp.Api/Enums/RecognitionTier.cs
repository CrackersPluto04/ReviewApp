namespace ReviewApp.Api.Enums;

// How closely a suggested media item relates to the object in the photo, closest first.
// The order of the values is the ranking order.
public enum RecognitionTier
{
    Direct,   // the work the object itself comes from (the Iron Man films for an Iron Man statue)
    Universe, // other works of the same franchise / universe (the Avengers films, other MCU titles)
    People,   // other work of an actor, director or musician connected to the object
    Similar   // loosely related works of the same theme, only when the closer tiers are thin
}
