using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Services;

/// <summary>
/// Hosted service that drains <see cref="EmailQueue"/> in the background and hands each message to the
/// <see cref="IEmailSender"/>. A failed message is retried twice (after 2 s and 10 s) and then dropped with a log
/// entry; it never blocks the messages behind it for long and never affects the request that queued it.
/// </summary>
public class EmailDispatcherService : BackgroundService
{
    private static readonly TimeSpan[] DefaultRetryDelays = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10)];

    private readonly EmailQueue _queue;
    private readonly IEmailSender _sender;
    private readonly ILogger<EmailDispatcherService> _logger;
    private readonly TimeSpan[] _retryDelays;

    public EmailDispatcherService(EmailQueue queue, IEmailSender sender, ILogger<EmailDispatcherService> logger)
        : this(queue, sender, logger, DefaultRetryDelays) { }

    // Tests pass short delays
    internal EmailDispatcherService(EmailQueue queue, IEmailSender sender, ILogger<EmailDispatcherService> logger, TimeSpan[] retryDelays)
    {
        _queue = queue;
        _sender = sender;
        _logger = logger;
        _retryDelays = retryDelays;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in _queue.ReadAllAsync(stoppingToken))
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    await _sender.SendAsync(message, stoppingToken);
                    break;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    if (attempt >= _retryDelays.Length)
                    {
                        // Subject only, the body contains tokens and codes
                        _logger.LogError(ex, "Failed to send email \"{Subject}\", giving up", message.Subject);
                        break;
                    }

                    _logger.LogWarning(ex, "Failed to send email \"{Subject}\", retrying", message.Subject);
                    await Task.Delay(_retryDelays[attempt], stoppingToken);
                }
            }
        }
    }
}
