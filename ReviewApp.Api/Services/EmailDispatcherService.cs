using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Services;

// Sends the queued emails one by one, retrying a few times before giving up on a message
public class EmailDispatcherService : BackgroundService
{
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10)];

    private readonly EmailQueue _queue;
    private readonly IEmailSender _sender;
    private readonly ILogger<EmailDispatcherService> _logger;

    public EmailDispatcherService(EmailQueue queue, IEmailSender sender, ILogger<EmailDispatcherService> logger)
    {
        _queue = queue;
        _sender = sender;
        _logger = logger;
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
                    if (attempt >= RetryDelays.Length)
                    {
                        // Subject only, the body contains tokens and codes
                        _logger.LogError(ex, "Failed to send email \"{Subject}\", giving up", message.Subject);
                        break;
                    }

                    _logger.LogWarning(ex, "Failed to send email \"{Subject}\", retrying", message.Subject);
                    await Task.Delay(RetryDelays[attempt], stoppingToken);
                }
            }
        }
    }
}
