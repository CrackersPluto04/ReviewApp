namespace ReviewApp.Api.DTOs;

// One achievement card: all tiers of a GroupCode with the user's progress
public record AchievementDto
{
    public string GroupCode { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string IconKey { get; init; } = string.Empty;
    public List<string> Categories { get; init; } = [];
    // Shared by every tier, they all count the same metric
    public int CurrentProgress { get; init; }
    // Ordered bronze -> gold
    public List<AchievementTierDto> Tiers { get; init; } = [];
}

public record AchievementTierDto
{
    public string Tier { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int TargetValue { get; init; }
    public bool IsUnlocked { get; init; }
}

// Sent back to the client (X-Unlocked-Achievements header) when an action unlocks new tiers
public record UnlockedAchievementDto
{
    public string GroupCode { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Tier { get; init; } = string.Empty;
}
