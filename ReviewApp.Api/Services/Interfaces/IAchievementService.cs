using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;

namespace ReviewApp.Api.Services.Interfaces;

public interface IAchievementService
{
    // Recalculates the user's progress for the given metrics and returns the newly unlocked tiers.
    // Never throws: a failure is logged and returns an empty list, so the calling action still succeeds.
    Task<IReadOnlyList<UnlockedAchievementDto>> EvaluateAsync(int userId, params AchievementMetric[] metrics);

    // Every achievement with the user's progress, null if the user does not exist
    Task<List<AchievementDto>?> GetUserAchievementsAsync(string username);
}
