using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ReviewApp.Api.Services;
using ReviewApp.Api.Services.Interfaces;
using ReviewApp.Api.Tests.Support;

namespace ReviewApp.Api.Tests.Unit;

// EmailQueue -> EmailDispatcherService -> IEmailSender, with the retry delays shortened
public class EmailPipelineTests
{
    private static readonly TimeSpan[] ShortDelays = [TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1)];

    private static EmailMessage Message(string subject) => new("user@test.local", subject, "<p>html</p>", "text");

    private static async Task RunDispatcherAsync(EmailQueue queue, IEmailSender sender, Func<bool> until)
    {
        var dispatcher = new EmailDispatcherService(queue, sender, NullLogger<EmailDispatcherService>.Instance, ShortDelays);
        await dispatcher.StartAsync(CancellationToken.None);
        try
        {
            await TestWait.UntilAsync(until, timeoutMs: 5000);
        }
        finally
        {
            await dispatcher.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Dispatcher_SendsQueuedEmailsInOrder()
    {
        var queue = new EmailQueue(NullLogger<EmailQueue>.Instance);
        var sender = new CapturingEmailSender();
        queue.Enqueue(Message("first"));
        queue.Enqueue(Message("second"));

        await RunDispatcherAsync(queue, sender, () => sender.Inbox("user@test.local").Count == 2);

        sender.Inbox("user@test.local").Select(m => m.Subject).ShouldBe(["first", "second"]);
    }

    [Fact]
    public async Task Dispatcher_RetriesAFailedSend()
    {
        var queue = new EmailQueue(NullLogger<EmailQueue>.Instance);
        var sender = Substitute.For<IEmailSender>();
        var calls = 0;
        sender.SendAsync(default!, default).ReturnsForAnyArgs(_ =>
            Interlocked.Increment(ref calls) < 3 ? Task.FromException(new IOException("smtp down")) : Task.CompletedTask);
        queue.Enqueue(Message("retried"));

        await RunDispatcherAsync(queue, sender, () => Volatile.Read(ref calls) == 3);

        calls.ShouldBe(3);
    }

    [Fact]
    public async Task Dispatcher_GivesUpOnAMessageAndMovesOn()
    {
        var queue = new EmailQueue(NullLogger<EmailQueue>.Instance);
        var sender = Substitute.For<IEmailSender>();
        var sent = new List<string>();
        sender.SendAsync(default!, default).ReturnsForAnyArgs(call =>
        {
            var message = call.Arg<EmailMessage>();
            if (message.Subject == "broken") return Task.FromException(new IOException("rejected"));
            lock (sent) sent.Add(message.Subject);
            return Task.CompletedTask;
        });
        queue.Enqueue(Message("broken"));
        queue.Enqueue(Message("next"));

        await RunDispatcherAsync(queue, sender, () => { lock (sent) return sent.Count == 1; });

        // One try plus the two retries for the broken one, then the next message still goes out
        await sender.Received(3).SendAsync(Arg.Is<EmailMessage>(m => m.Subject == "broken"), Arg.Any<CancellationToken>());
        sent.ShouldBe(["next"]);
    }

    [Fact]
    public void Queue_WhenFull_DropsAndLogsTheEmail()
    {
        var logger = new ListLogger<EmailQueue>();
        var queue = new EmailQueue(logger);

        for (var i = 0; i < 501; i++)
            queue.Enqueue(Message($"email {i}"));

        // The subject is logged, never the body (it contains tokens)
        logger.Errors.ShouldHaveSingleItem().ShouldContain("email 500");
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
                Errors.Add(formatter(state, exception));
        }
    }
}
