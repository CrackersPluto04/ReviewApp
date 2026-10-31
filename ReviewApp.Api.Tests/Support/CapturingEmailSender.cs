using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using ReviewApp.Api.Services;
using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Tests.Support;

// Stands in for SMTP behind the real EmailQueue -> EmailDispatcherService path and keeps every email per recipient.
// Recipients are unique per test, so parallel tests only ever see their own mail.
public partial class CapturingEmailSender : IEmailSender
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<string, ConcurrentQueue<EmailMessage>> _inboxes = new(StringComparer.OrdinalIgnoreCase);

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        _inboxes.GetOrAdd(message.To, _ => new ConcurrentQueue<EmailMessage>()).Enqueue(message);
        return Task.CompletedTask;
    }

    public IReadOnlyList<EmailMessage> Inbox(string recipient) =>
        _inboxes.TryGetValue(recipient, out var inbox) ? inbox.ToList() : [];

    // Sending happens in the background, so wait until the expected email arrived
    public async Task<EmailMessage> WaitForAsync(string recipient, string subjectContains)
    {
        var deadline = DateTime.UtcNow + WaitTimeout;
        while (DateTime.UtcNow < deadline)
        {
            var match = Inbox(recipient).LastOrDefault(m => m.Subject.Contains(subjectContains, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
            await Task.Delay(25);
        }

        throw new TimeoutException($"No email \"{subjectContains}\" arrived for {recipient}. Inbox: " +
            string.Join(", ", Inbox(recipient).Select(m => m.Subject)));
    }

    // For "nothing was sent": gives the background dispatcher a moment to deliver anything that was queued
    public async Task<IReadOnlyList<EmailMessage>> SettledInboxAsync(string recipient)
    {
        await Task.Delay(300);
        return Inbox(recipient);
    }

    public static string LinkToken(EmailMessage message) =>
        Uri.UnescapeDataString(TokenPattern().Match(message.TextBody) is { Success: true } match
            ? match.Groups[1].Value
            : throw new InvalidOperationException($"No link token in \"{message.Subject}\"."));

    public static string Code(EmailMessage message) =>
        CodePattern().Match(message.TextBody) is { Success: true } match
            ? match.Value
            : throw new InvalidOperationException($"No code in \"{message.Subject}\".");

    [GeneratedRegex(@"[?&]token=([^\s&]+)")]
    private static partial Regex TokenPattern();

    [GeneratedRegex(@"(?m)^\d{6}(?=\r?$)")]
    private static partial Regex CodePattern();
}
