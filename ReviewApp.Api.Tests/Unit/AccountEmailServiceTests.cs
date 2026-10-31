using Microsoft.Extensions.Logging.Abstractions;
using ReviewApp.Api.Services;
using ReviewApp.Api.Services.Interfaces;
using ReviewApp.Api.Tests.Support;

namespace ReviewApp.Api.Tests.Unit;

public class AccountEmailServiceTests
{
    private const string To = "user@test.local";
    private const string Name = "alice";

    private readonly EmailQueue _queue = new(NullLogger<EmailQueue>.Instance);
    private readonly AccountEmailService _service;

    public AccountEmailServiceTests()
    {
        _service = new AccountEmailService(_queue, TestTokens.Configuration);
    }

    // The service only enqueues, so the message is already in the queue when the call returns
    private async Task<EmailMessage> SentAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var message in _queue.ReadAllAsync(timeout.Token))
            return message;
        throw new InvalidOperationException("Nothing was queued.");
    }

    [Fact]
    public async Task SendWelcome_LinksToVerifyPageWithEscapedToken()
    {
        _service.SendWelcome(To, Name, "a+b/c=");

        var message = await SentAsync();
        message.To.ShouldBe(To);
        message.Subject.ShouldBe("Welcome to THE Review App - please verify your email");
        message.TextBody.ShouldContain($"{ApiFactory.FrontendBaseUrl}/verify-email?token=a%2Bb%2Fc%3D");
        message.TextBody.ShouldContain("24 hours");
        message.TextBody.ShouldContain(Name);
    }

    [Fact]
    public async Task SendVerification_LinksToVerifyPage()
    {
        _service.SendVerification(To, Name, "tok");

        var message = await SentAsync();
        message.Subject.ShouldBe("Verify your email address");
        message.TextBody.ShouldContain("/verify-email?token=tok");
    }

    [Fact]
    public async Task SendEmailChangeConfirmation_GoesToNewAddressWithConfirmLink()
    {
        _service.SendEmailChangeConfirmation("new@test.local", Name, "tok");

        var message = await SentAsync();
        message.To.ShouldBe("new@test.local");
        message.Subject.ShouldBe("Confirm your new email address");
        message.TextBody.ShouldContain("/confirm-email-change?token=tok");
        message.TextBody.ShouldContain("1 hour");
    }

    [Fact]
    public async Task SendEmailChangeRequestedNotice_TellsOldAddressWithoutAnyLink()
    {
        _service.SendEmailChangeRequestedNotice("old@test.local", Name, "new@test.local");

        var message = await SentAsync();
        message.To.ShouldBe("old@test.local");
        message.Subject.ShouldBe("Your email address is being changed");
        message.TextBody.ShouldContain("new@test.local");
        message.TextBody.ShouldNotContain("token=");
    }

    [Fact]
    public async Task SendPasswordChanged_SaysOtherDevicesWereLoggedOut()
    {
        _service.SendPasswordChanged(To, Name);

        var message = await SentAsync();
        message.Subject.ShouldBe("Your password was changed");
        message.TextBody.ShouldContain("All other devices were logged out.");
    }

    [Theory]
    [InlineData(TwoFactorCodePurpose.Login, "Your login code")]
    [InlineData(TwoFactorCodePurpose.Enable, "Your code to enable two-factor authentication")]
    [InlineData(TwoFactorCodePurpose.Disable, "Your code to disable two-factor authentication")]
    public async Task SendTwoFactorCode_ShowsTheCodeWithPurposeSubject(TwoFactorCodePurpose purpose, string subject)
    {
        _service.SendTwoFactorCode(To, Name, "012345", purpose);

        var message = await SentAsync();
        message.Subject.ShouldBe(subject);
        CapturingEmailSender.Code(message).ShouldBe("012345");
        message.TextBody.ShouldContain("10 minutes");
    }

    [Theory]
    [InlineData(true, "Two-factor authentication enabled")]
    [InlineData(false, "Two-factor authentication disabled")]
    public async Task SendTwoFactorStatusChanged_SubjectFollowsTheNewState(bool enabled, string subject)
    {
        _service.SendTwoFactorStatusChanged(To, Name, enabled);

        (await SentAsync()).Subject.ShouldBe(subject);
    }

    [Fact]
    public async Task SendPremiumStarted_MentionsRenewalDateAndLinksToPremium()
    {
        _service.SendPremiumStarted(To, Name, new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc));

        var message = await SentAsync();
        message.Subject.ShouldBe("Welcome to Premium");
        message.TextBody.ShouldContain("December 1, 2026");
        message.TextBody.ShouldContain($"{ApiFactory.FrontendBaseUrl}/premium");
    }

    [Fact]
    public async Task SendPaymentFailed_MentionsGraceEnd()
    {
        _service.SendPaymentFailed(To, Name, new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc));

        var message = await SentAsync();
        message.Subject.ShouldBe("We couldn't renew your Premium subscription");
        message.TextBody.ShouldContain("November 5, 2026");
    }

    [Fact]
    public async Task SendSubscriptionCancelled_WithoutDate_StillReads()
    {
        _service.SendSubscriptionCancelled(To, Name, null);

        var message = await SentAsync();
        message.Subject.ShouldBe("Your Premium subscription was cancelled");
        message.TextBody.ShouldContain("You can resume it before it ends.");
    }

    [Fact]
    public async Task SendSubscriptionEnded_LinksToPremium()
    {
        _service.SendSubscriptionEnded(To, Name);

        var message = await SentAsync();
        message.Subject.ShouldBe("Your Premium subscription has ended");
        message.TextBody.ShouldContain("See Premium: https://frontend.test/premium");
    }

    [Fact]
    public async Task UserInput_IsHtmlEncodedInTheHtmlBody()
    {
        _service.SendPasswordChanged(To, "<b>bob</b>");

        var message = await SentAsync();
        message.HtmlBody.ShouldNotContain("<b>bob</b>");
        message.HtmlBody.ShouldContain("&lt;b&gt;bob&lt;/b&gt;");
    }
}
