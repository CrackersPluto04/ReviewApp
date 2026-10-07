using ReviewApp.Api.DTOs;

namespace ReviewApp.Api.Services.Interfaces;

// Reacts to newly unlocked achievement tiers. Register more implementations
// (e.g. notifications later on) instead of changing AchievementService.
public interface IAchievementUnlockHandler
{
    Task HandleAsync(int userId, IReadOnlyList<UnlockedAchievementDto> unlocked);
}
