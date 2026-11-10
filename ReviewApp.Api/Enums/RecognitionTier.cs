namespace ReviewApp.Api.Enums;

/// <summary>
/// How closely a suggested media item relates to the object in the photo, closest first. The order of the values
/// is the ranking order: <c>RecognitionRanking</c> compares tiers with the default enum ordering, so reordering
/// them changes the results.
/// </summary>
public enum RecognitionTier
{
    /// <summary>The work the object itself comes from (the Iron Man films for an Iron Man statue).</summary>
    Direct,
    /// <summary>Other works of the same franchise or universe (the Avengers films, other MCU titles).</summary>
    Universe,
    /// <summary>Other work of an actor, director or musician connected to the object.</summary>
    People,
    /// <summary>Loosely related works of the same theme, only used when the closer tiers are thin.</summary>
    Similar
}
