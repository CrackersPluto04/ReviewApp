using System.Threading.Channels;

namespace ReviewApp.Api.Services;

/// <summary>
/// An email ready to send, with both an HTML and a plain-text body.
/// </summary>
public record EmailMessage(string To, string Subject, string HtmlBody, string TextBody);

/// <summary>
/// In-process queue (singleton) between the request threads and <c>EmailDispatcherService</c>. Requests only
/// enqueue, so a slow or broken mail server never slows down or fails them. Queued emails are lost on restart,
/// which is acceptable because every email-dependent flow has a resend action.
/// </summary>
public class EmailQueue
{
    private const int Capacity = 500;

    // Wait mode only so that TryWrite reports a full queue (DropWrite drops silently and still returns true);
    // nothing awaits a write, a full queue drops the email either way
    private readonly Channel<EmailMessage> _channel = Channel.CreateBounded<EmailMessage>(new BoundedChannelOptions(Capacity)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true
    });

    private readonly ILogger<EmailQueue> _logger;

    public EmailQueue(ILogger<EmailQueue> logger)
    {
        _logger = logger;
    }

    /// <summary>Adds an email to the queue. Never blocks or throws; a full queue (500) drops the email and logs it.</summary>
    public void Enqueue(EmailMessage message)
    {
        // Never log the body, it contains tokens and codes
        if (!_channel.Writer.TryWrite(message))
            _logger.LogError("Email queue is full, dropped email \"{Subject}\"", message.Subject);
    }

    /// <summary>Reads queued emails as they arrive. Meant for a single reader, the dispatcher.</summary>
    public IAsyncEnumerable<EmailMessage> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
