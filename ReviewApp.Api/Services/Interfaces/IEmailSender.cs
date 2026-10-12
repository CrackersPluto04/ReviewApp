using ReviewApp.Api.Services;

namespace ReviewApp.Api.Services.Interfaces;

// Delivers one email right away. Callers don't use it directly, they go through IAccountEmailService,
// which only enqueues; EmailDispatcherService sends from the queue in the background.
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
