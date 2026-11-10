using ReviewApp.Api.Services;

namespace ReviewApp.Api.Services.Interfaces;

/// <summary>
/// Delivers one email right away. Callers do not use it directly: they go through <see cref="IAccountEmailService"/>,
/// which only enqueues, and <c>EmailDispatcherService</c> sends from the queue in the background.
/// </summary>
public interface IEmailSender
{
    /// <summary>Sends the message. Throws when delivery fails, which the dispatcher retries a few times.</summary>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
