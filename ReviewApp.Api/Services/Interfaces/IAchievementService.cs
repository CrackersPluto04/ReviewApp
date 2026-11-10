using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;

namespace ReviewApp.Api.Services.Interfaces;

/// <summary>
/// Tracks achievement progress and unlocks. Unlocks are permanent and progress is the highest count ever
/// reached, so deleting content never takes anything away.
/// </summary>
public interface IAchievementService
{
    /// <summary>
    /// Recounts the user's real totals for the given metrics, raises the stored progress to them (it never goes
    /// down) and reports the tiers that became unlocked. Call it after <c>SaveChangesAsync</c> when content is
    /// created or a review is edited, not on deletes. Never throws: a failure is logged and an empty list is
    /// returned, so the action that triggered it still succeeds.
    /// </summary>
    /// <param name="userId">The user whose progress is updated.</param>
    /// <param name="metrics">The counters the triggering action can have changed.</param>
    /// <returns>The newly unlocked tiers, also handed to every <see cref="IAchievementUnlockHandler"/>.</returns>
    Task<IReadOnlyList<UnlockedAchievementDto>> EvaluateAsync(int userId, params AchievementMetric[] metrics);

    /// <summary>
    /// Lists every achievement with the user's progress; achievements with no progress yet are included.
    /// </summary>
    /// <param name="username">The profile owner's username.</param>
    /// <returns>The achievement cards, or null if the user does not exist.</returns>
    Task<List<AchievementDto>?> GetUserAchievementsAsync(string username);
}
