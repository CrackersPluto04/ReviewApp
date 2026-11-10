namespace ReviewApp.Api.DTOs;

/// <summary>
/// One achievement card: every tier of a <c>GroupCode</c> together with the user's progress.
/// </summary>
public record AchievementDto
{
    /// <summary>Stable key shared by the bronze, silver and gold tiers of this card.</summary>
    public string GroupCode { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    /// <summary>Names the icon file the frontend shows; unknown keys fall back to a trophy.</summary>
    public string IconKey { get; init; } = string.Empty;
    /// <summary>Category names. An achievement can belong to several categories.</summary>
    public List<string> Categories { get; init; } = [];
    /// <summary>
    /// The highest count the user has ever reached, shared by every tier because they all count the same metric.
    /// Deleting content never lowers it.
    /// </summary>
    public int CurrentProgress { get; init; }
    /// <summary>The tiers, ordered bronze to gold.</summary>
    public List<AchievementTierDto> Tiers { get; init; } = [];
}

/// <summary>
/// One tier of an achievement card.
/// </summary>
public record AchievementTierDto
{
    public string Tier { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    /// <summary>The count that unlocks this tier.</summary>
    public int TargetValue { get; init; }
    /// <summary>Unlocks are permanent: stays true even if the count later drops.</summary>
    public bool IsUnlocked { get; init; }
}

/// <summary>
/// A tier that an action has just unlocked. Sent to the requesting user in the <c>X-Unlocked-Achievements</c>
/// response header, so the clients can show a toast without extra requests.
/// </summary>
public record UnlockedAchievementDto
{
    public string GroupCode { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Tier { get; init; } = string.Empty;
}
