using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Services;

/// <summary>
/// Safety net for missed provider notifications, run every 15 minutes: ends subscriptions whose period (and grace)
/// is over. Entitlement checks already use the stored period end, so a missed notification never grants Premium
/// for free; this only corrects the stored status and sends the "ended" notice.
/// </summary>
public class SubscriptionExpiryService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SubscriptionExpiryService> _logger;

    public SubscriptionExpiryService(IServiceScopeFactory scopeFactory, ILogger<SubscriptionExpiryService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var subscriptionService = scope.ServiceProvider.GetRequiredService<ISubscriptionService>();

                var ended = await subscriptionService.ExpireLapsedAsync(stoppingToken);
                if (ended > 0)
                    _logger.LogInformation("Subscription expiry ended {Count} subscriptions", ended);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Subscription expiry failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
