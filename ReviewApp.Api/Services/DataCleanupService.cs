using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.Services.Interfaces;
using ReviewApp.Api.Services.Sync;

namespace ReviewApp.Api.Services;

/// <summary>
/// Daily housekeeping: deletes old seen notifications, spent security tokens, old recognition usage rows and the
/// sync bookkeeping (operations and tombstones) past its retention, and repairs media rows left incomplete by
/// failed lookups. Runs once right after startup and then every day. Each delete is a set-based statement, so it
/// does not load rows into memory.
/// </summary>
public class DataCleanupService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);
    private static readonly TimeSpan SeenNotificationRetention = TimeSpan.FromDays(90);
    // Kept a little longer than needed, so "already used" / "expired" answers stay accurate for a while
    private static readonly TimeSpan SpentTokenRetention = TimeSpan.FromDays(1);
    // The daily allowance only looks at the current day; a few days are kept for debugging
    internal static readonly TimeSpan RecognitionUsageRetention = TimeSpan.FromDays(7);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DataCleanupService> _logger;

    public DataCleanupService(IServiceScopeFactory scopeFactory, ILogger<DataCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        // First run right after startup, then once a day
        do
        {
            try
            {
                await CleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Data cleanup failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    // Internal so the tests can run one cleanup pass directly
    internal async Task CleanupAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;

        var notificationCutoff = now - SeenNotificationRetention;
        var notifications = await context.Notifications
            .Where(n => n.IsSeen && n.CreatedAt < notificationCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        var tokenCutoff = now - SpentTokenRetention;
        var tokens = await context.SecurityTokens
            .Where(t => t.ExpiresAt < tokenCutoff || (t.IsUsed && t.CreatedAt < tokenCutoff))
            .ExecuteDeleteAsync(cancellationToken);

        var usageCutoff = now - RecognitionUsageRetention;
        var usages = await context.RecognitionUsages
            .Where(u => u.CreatedAt < usageCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        var syncCutoff = now - SyncRetention.Retention;
        var syncOperations = await context.SyncOperations
            .Where(o => o.CreatedAt < syncCutoff)
            .ExecuteDeleteAsync(cancellationToken);
        var tombstones = await context.SyncTombstones
            .Where(t => t.DeletedAt < syncCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        if (notifications > 0 || tokens > 0 || usages > 0 || syncOperations > 0 || tombstones > 0)
            _logger.LogInformation("Data cleanup removed {Notifications} notifications, {Tokens} security tokens, {Usages} recognition usage rows, " +
                "{SyncOperations} sync operations and {Tombstones} sync tombstones",
                notifications, tokens, usages, syncOperations, tombstones);

        await RepairIncompleteMediaAsync(context, scope.ServiceProvider.GetRequiredService<IMediaService>(), cancellationToken);
    }

    // Media rows stored from a failed TMDb / Spotify lookup by older versions have no real title.
    // Unused ones are deleted; the others are fetched again (a few per run, to spare the API quotas).
    private const int MediaRepairsPerRun = 50;

    private async Task RepairIncompleteMediaAsync(AppDbContext context, IMediaService mediaService, CancellationToken cancellationToken)
    {
        var deleted = await IncompleteMedia(context)
            .Where(m => !context.Reviews.Any(r => r.MediaID == m.ID) && !context.CollectionMedias.Any(cm => cm.MediaID == m.ID))
            .ExecuteDeleteAsync(cancellationToken);

        var used = await IncompleteMedia(context).OrderBy(m => m.ID).Take(MediaRepairsPerRun).ToListAsync(cancellationToken);
        int repaired = 0, unknown = 0;
        foreach (var media in used)
        {
            var lookup = await mediaService.LookupAsync(media.MediaType, media.ExternalApiID, cancellationToken);
            if (lookup.Status == MediaLookupStatus.Unavailable)
                break; // the source is down, try again tomorrow
            if (lookup.Status == MediaLookupStatus.Found)
            {
                MediaService.ApplyDetails(media, lookup.Media!);
                repaired++;
            }
            else
            {
                unknown++;
            }
        }
        await context.SaveChangesAsync(cancellationToken);

        if (deleted > 0 || repaired > 0 || unknown > 0)
            _logger.LogInformation("Media repair deleted {Deleted} unused incomplete rows, repaired {Repaired}, {Unknown} still unknown at the source",
                deleted, repaired, unknown);
    }

    internal const string IncompleteTitle = "Unknown Title";

    private static IQueryable<Media> IncompleteMedia(AppDbContext context) =>
        context.Media.Where(m => m.Title == null || m.Title == "" || m.Title == IncompleteTitle);
}
