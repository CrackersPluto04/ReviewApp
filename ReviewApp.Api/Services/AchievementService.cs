using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Services;

public class AchievementService : IAchievementService
{
    // Created by the AddDefaultCollection trigger, doesn't count as a collection made by the user
    private const string DefaultCollectionName = "Favourites";
    private const decimal LowScoreLimit = 5.0m;

    private readonly AppDbContext _context;
    private readonly IEnumerable<IAchievementUnlockHandler> _unlockHandlers;
    private readonly ILogger<AchievementService> _logger;

    public AchievementService(AppDbContext context, IEnumerable<IAchievementUnlockHandler> unlockHandlers, ILogger<AchievementService> logger)
    {
        _context = context;
        _unlockHandlers = unlockHandlers;
        _logger = logger;
    }

    public async Task<IReadOnlyList<UnlockedAchievementDto>> EvaluateAsync(int userId, params AchievementMetric[] metrics)
    {
        if (metrics.Length == 0)
            return [];

        List<UnlockedAchievementDto> unlocked;
        try
        {
            unlocked = await UpdateProgressAsync(userId, metrics.Distinct().ToList());
        }
        catch (Exception ex)
        {
            // Achievements must never break the action that triggered them (review, reply, collection).
            // A DbUpdateException here is usually a concurrent request creating the same progress row,
            // the next evaluation of that metric fixes the counts anyway.
            _logger.LogError(ex, "Achievement evaluation failed for user {UserId} ({Metrics})", userId, string.Join(", ", metrics));
            _context.ChangeTracker.Clear();
            return [];
        }

        if (unlocked.Count > 0)
            await NotifyHandlersAsync(userId, unlocked);

        return unlocked;
    }

    public async Task<List<AchievementDto>?> GetUserAchievementsAsync(string username)
    {
        var userId = await _context.Users
            .Where(u => u.Username == username)
            .Select(u => (int?)u.ID)
            .FirstOrDefaultAsync();

        if (userId == null)
            return null;

        var definitions = await _context.Achievements
            .AsNoTracking()
            .OrderBy(a => a.ID)
            .ToListAsync();

        // Missing rows mean no progress yet
        var progress = await _context.UserAchievements
            .AsNoTracking()
            .Where(ua => ua.UserID == userId.Value)
            .ToDictionaryAsync(ua => ua.AchievementID);

        // GroupBy keeps the order of first appearance, so cards follow the seed order
        return definitions
            .GroupBy(a => a.GroupCode)
            .Select(group =>
            {
                var tiers = group.OrderBy(a => a.Tier).ToList();
                var first = tiers[0];

                return new AchievementDto
                {
                    GroupCode = group.Key,
                    Title = first.Title,
                    IconKey = first.IconKey,
                    Categories = CategoryNames(first.Categories),
                    CurrentProgress = tiers.Max(t => progress.TryGetValue(t.ID, out var p) ? p.CurrentProgress : 0),
                    Tiers = tiers.Select(t => new AchievementTierDto
                    {
                        Tier = t.Tier.ToString(),
                        Description = t.Description,
                        TargetValue = t.TargetValue,
                        IsUnlocked = progress.TryGetValue(t.ID, out var p) && p.IsUnlocked
                    }).ToList()
                };
            })
            .ToList();
    }

    private async Task<List<UnlockedAchievementDto>> UpdateProgressAsync(int userId, List<AchievementMetric> metrics)
    {
        var definitions = await _context.Achievements
            .Where(a => metrics.Contains(a.Metric))
            .OrderBy(a => a.ID)
            .ToListAsync();

        if (definitions.Count == 0)
            return [];

        var definitionIds = definitions.Select(a => a.ID).ToList();
        var rows = await _context.UserAchievements
            .Where(ua => ua.UserID == userId && definitionIds.Contains(ua.AchievementID))
            .ToDictionaryAsync(ua => ua.AchievementID);

        // One DbContext can't run queries in parallel, so count one metric after the other
        var counts = new Dictionary<AchievementMetric, int>();
        foreach (var metric in metrics)
            counts[metric] = Math.Max(0, await CountAsync(userId, metric));

        var unlocked = new List<UnlockedAchievementDto>();
        foreach (var definition in definitions)
        {
            var count = counts[definition.Metric];

            if (!rows.TryGetValue(definition.ID, out var row))
            {
                // Don't create empty rows, no row already means zero progress
                if (count == 0)
                    continue;

                row = new UserAchievement { UserID = userId, AchievementID = definition.ID };
                _context.UserAchievements.Add(row);
            }

            row.CurrentProgress = count;

            // Unlocks are permanent, deleting content later only lowers the progress
            if (!row.IsUnlocked && count >= definition.TargetValue)
            {
                row.IsUnlocked = true;
                unlocked.Add(new UnlockedAchievementDto
                {
                    GroupCode = definition.GroupCode,
                    Title = definition.Title,
                    Tier = definition.Tier.ToString()
                });
            }
        }

        await _context.SaveChangesAsync();
        return unlocked;
    }

    // Keep in sync with the backfill SQL in the AddAchievements migration
    private Task<int> CountAsync(int userId, AchievementMetric metric) => metric switch
    {
        AchievementMetric.MovieReviews => _context.Reviews.CountAsync(r => r.UserID == userId && r.Media.MediaType == MediaType.Movie),
        AchievementMetric.SeriesReviews => _context.Reviews.CountAsync(r => r.UserID == userId && r.Media.MediaType == MediaType.Series),
        AchievementMetric.MusicReviews => _context.Reviews.CountAsync(r => r.UserID == userId && r.Media.MediaType == MediaType.Music),
        AchievementMetric.TotalReviews => _context.Reviews.CountAsync(r => r.UserID == userId),
        AchievementMetric.Replies => _context.ReviewReplies.CountAsync(rr => rr.UserID == userId && !rr.IsDeleted),
        AchievementMetric.LowScoreReviews => _context.Reviews.CountAsync(r => r.UserID == userId && r.Score <= LowScoreLimit),
        AchievementMetric.CollectionsCreated => _context.Collections.CountAsync(c => c.UserID == userId && c.Name != DefaultCollectionName),
        _ => Task.FromResult(0)
    };

    private async Task NotifyHandlersAsync(int userId, IReadOnlyList<UnlockedAchievementDto> unlocked)
    {
        foreach (var handler in _unlockHandlers)
        {
            try
            {
                await handler.HandleAsync(userId, unlocked);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Achievement unlock handler {Handler} failed for user {UserId}", handler.GetType().Name, userId);
            }
        }
    }

    private static List<string> CategoryNames(AchievementCategories categories) =>
        Enum.GetValues<AchievementCategories>()
            .Where(c => c != AchievementCategories.None && categories.HasFlag(c))
            .Select(c => c.ToString())
            .ToList();
}
