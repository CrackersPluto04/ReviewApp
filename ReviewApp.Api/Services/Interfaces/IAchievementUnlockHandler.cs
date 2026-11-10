using ReviewApp.Api.DTOs;

namespace ReviewApp.Api.Services.Interfaces;

/// <summary>
/// Reacts to newly unlocked achievement tiers. This is the extension point for new reactions: register another
/// implementation instead of changing <c>AchievementService</c>.
/// </summary>
public interface IAchievementUnlockHandler
{
    /// <summary>Handles one evaluation's unlocks for one user.</summary>
    /// <param name="userId">The user who unlocked the tiers.</param>
    /// <param name="unlocked">The tiers unlocked by this evaluation; never empty.</param>
    Task HandleAsync(int userId, IReadOnlyList<UnlockedAchievementDto> unlocked);
}
