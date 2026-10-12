using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL;

namespace ReviewApp.Api.Services;

// Daily housekeeping: old seen notifications and spent security tokens
public class DataCleanupService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);
    private static readonly TimeSpan SeenNotificationRetention = TimeSpan.FromDays(90);
    // Kept a little longer than needed, so "already used" / "expired" answers stay accurate for a while
    private static readonly TimeSpan SpentTokenRetention = TimeSpan.FromDays(1);

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

    private async Task CleanupAsync(CancellationToken cancellationToken)
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

        if (notifications > 0 || tokens > 0)
            _logger.LogInformation("Data cleanup removed {Notifications} notifications and {Tokens} security tokens", notifications, tokens);
    }
}
