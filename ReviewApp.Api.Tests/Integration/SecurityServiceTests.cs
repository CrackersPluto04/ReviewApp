using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services;
using ReviewApp.Api.Services.Interfaces;
using ReviewApp.Api.Tests.Support;

namespace ReviewApp.Api.Tests.Integration;

public class SecurityServiceTests : IntegrationTest
{
    private ISecurityService Security => Service<ISecurityService>();

    private Task<List<SecurityToken>> TokensAsync(User user, SecurityTokenType type) =>
        Query().SecurityTokens.Where(t => t.UserID == user.ID && t.Type == type).OrderBy(t => t.ID).ToListAsync();

    private Task<User> ReloadAsync(User user) => Query().Users.SingleAsync(u => u.ID == user.ID);

    // Moves the user's tokens of a type back in time, past the resend cooldown
    private Task AgeTokensAsync(User user, SecurityTokenType type) =>
        Query().SecurityTokens.Where(t => t.UserID == user.ID && t.Type == type)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.CreatedAt, t => t.CreatedAt.AddMinutes(-2)));

    /* Registration and email verification */

    [Fact]
    public async Task OnRegistered_StoresOnlyTheHashAndMailsTheLink()
    {
        var user = await Data.UserAsync(u => u.IsEmailConfirmed = false);

        await Security.OnRegisteredAsync(user);

        var token = (await TokensAsync(user, SecurityTokenType.EmailConfirmation)).Single();
        var email = await App.Mail.WaitForAsync(user.Email, "Welcome to THE Review App");
        var raw = CapturingEmailSender.LinkToken(email);
        token.TokenHash.ShouldBe(SecurityTokens.Hash(raw));
        token.TokenHash.ShouldNotBe(raw);
        token.ExpiresAt.ShouldBe(DateTime.UtcNow.AddHours(24), TimeSpan.FromMinutes(1));
        (await Query().Notifications.AnyAsync(n => n.RecipientUserID == user.ID && n.Type == NotificationType.System)).ShouldBeTrue();
    }

    [Fact]
    public async Task ConfirmEmail_ValidLink_VerifiesAndBurnsTheToken()
    {
        var user = await Data.UserAsync(u => u.IsEmailConfirmed = false);
        var (_, raw) = await Data.SecurityTokenAsync(user, SecurityTokenType.EmailConfirmation);

        var (success, _) = await Security.ConfirmEmailAsync(raw);

        success.ShouldBeTrue();
        (await ReloadAsync(user)).IsEmailConfirmed.ShouldBeTrue();
        (await TokensAsync(user, SecurityTokenType.EmailConfirmation)).Single().IsUsed.ShouldBeTrue();
    }

    [Fact]
    public async Task ConfirmEmail_UnknownExpiredOrUsedLink_IsRejected()
    {
        var user = await Data.UserAsync(u => u.IsEmailConfirmed = false);
        var (_, expired) = await Data.SecurityTokenAsync(user, SecurityTokenType.EmailConfirmation, expiresAt: DateTime.UtcNow.AddMinutes(-1));
        var (_, used) = await Data.SecurityTokenAsync(user, SecurityTokenType.EmailConfirmation, used: true);

        (await Security.ConfirmEmailAsync(SecurityTokens.GenerateLinkToken())).Success.ShouldBeFalse();
        (await Security.ConfirmEmailAsync(expired)).Success.ShouldBeFalse();
        (await Security.ConfirmEmailAsync(used)).Success.ShouldBeFalse();
        (await ReloadAsync(user)).IsEmailConfirmed.ShouldBeFalse();
    }

    [Fact]
    public async Task ConfirmEmail_TokenOfAnotherType_IsRejected()
    {
        var user = await Data.UserAsync(u => u.IsEmailConfirmed = false);
        var (_, raw) = await Data.SecurityTokenAsync(user, SecurityTokenType.EmailChange, raw: SecurityTokens.GenerateLinkToken());

        (await Security.ConfirmEmailAsync(raw)).Success.ShouldBeFalse();
    }

    [Fact]
    public async Task ConfirmEmail_OpeningTheLinkAgain_IsHarmless()
    {
        var user = await Data.UserAsync(u => u.IsEmailConfirmed = false);
        var (_, raw) = await Data.SecurityTokenAsync(user, SecurityTokenType.EmailConfirmation);
        await Security.ConfirmEmailAsync(raw);

        var (success, message) = await Security.ConfirmEmailAsync(raw);

        success.ShouldBeTrue();
        message.ShouldBe("Your email address is already verified.");
    }

    [Fact]
    public async Task ResendVerification_AlreadyVerified_IsRejected()
    {
        var user = await Data.UserAsync();

        (await Security.ResendVerificationAsync(user.ID)).Success.ShouldBeFalse();
    }

    [Fact]
    public async Task ResendVerification_WithinCooldown_IsRejected()
    {
        var user = await Data.UserAsync(u => u.IsEmailConfirmed = false);
        await Data.SecurityTokenAsync(user, SecurityTokenType.EmailConfirmation, createdAt: DateTime.UtcNow.AddSeconds(-30));

        var (success, message) = await Security.ResendVerificationAsync(user.ID);

        success.ShouldBeFalse();
        message.ShouldBe("Please wait a minute before requesting another email.");
    }

    [Fact]
    public async Task ResendVerification_AfterCooldown_OnlyTheNewLinkWorks()
    {
        var user = await Data.UserAsync(u => u.IsEmailConfirmed = false);
        var (_, oldRaw) = await Data.SecurityTokenAsync(user, SecurityTokenType.EmailConfirmation, createdAt: DateTime.UtcNow.AddMinutes(-2));

        (await Security.ResendVerificationAsync(user.ID)).Success.ShouldBeTrue();

        var newRaw = CapturingEmailSender.LinkToken(await App.Mail.WaitForAsync(user.Email, "Verify your email address"));
        (await Security.ConfirmEmailAsync(oldRaw)).Success.ShouldBeFalse();
        (await Security.ConfirmEmailAsync(newRaw)).Success.ShouldBeTrue();
    }

    /* Email change */

    [Fact]
    public async Task RequestEmailChange_WrongPassword_IsRejectedFirst()
    {
        var user = await Data.UserAsync(u => u.IsEmailConfirmed = false);

        var (success, message) = await Security.RequestEmailChangeAsync(user.ID,
            new ChangeEmailDto { NewEmail = "new@test.local", CurrentPassword = "wrong" });

        success.ShouldBeFalse();
        message.ShouldBe("Incorrect password.");
    }

    [Fact]
    public async Task RequestEmailChange_NeedsVerifiedCurrentAddress()
    {
        var user = await Data.UserAsync(u => u.IsEmailConfirmed = false);

        var (success, _) = await Security.RequestEmailChangeAsync(user.ID,
            new ChangeEmailDto { NewEmail = $"{TestData.Unique("new")}@test.local", CurrentPassword = TestData.Password });

        success.ShouldBeFalse();
    }

    [Fact]
    public async Task RequestEmailChange_SameOrTakenAddress_IsRejected()
    {
        var user = await Data.UserAsync();
        var other = await Data.UserAsync();

        (await Security.RequestEmailChangeAsync(user.ID, new ChangeEmailDto { NewEmail = user.Email, CurrentPassword = TestData.Password }))
            .Message.ShouldBe("This is already your email address.");
        (await Security.RequestEmailChangeAsync(user.ID, new ChangeEmailDto { NewEmail = other.Email.ToUpperInvariant(), CurrentPassword = TestData.Password }))
            .Message.ShouldBe("Email already in use.");
    }

    [Fact]
    public async Task RequestEmailChange_StoresPendingAndMailsBothAddresses()
    {
        var user = await Data.UserAsync();
        var newEmail = $"{TestData.Unique("new")}@test.local";

        var (success, _) = await Security.RequestEmailChangeAsync(user.ID, new ChangeEmailDto { NewEmail = $" {newEmail} ", CurrentPassword = TestData.Password });

        success.ShouldBeTrue();
        (await ReloadAsync(user)).PendingEmail.ShouldBe(newEmail);
        await App.Mail.WaitForAsync(newEmail, "Confirm your new email address");
        await App.Mail.WaitForAsync(user.Email, "Your email address is being changed");
    }

    [Fact]
    public async Task ConfirmEmailChange_SwitchesAddressAndLogsOutEverywhere()
    {
        var user = await Data.UserAsync();
        await Data.SecurityTokenAsync(user, SecurityTokenType.EmailConfirmation); // an old verification link
        var newEmail = $"{TestData.Unique("new")}@test.local";
        await Security.RequestEmailChangeAsync(user.ID, new ChangeEmailDto { NewEmail = newEmail, CurrentPassword = TestData.Password });
        var raw = CapturingEmailSender.LinkToken(await App.Mail.WaitForAsync(newEmail, "Confirm your new email address"));

        var (success, _, changed) = await Security.ConfirmEmailChangeAsync(raw);

        success.ShouldBeTrue();
        changed!.ID.ShouldBe(user.ID);
        var stored = await ReloadAsync(user);
        stored.Email.ShouldBe(newEmail);
        stored.PendingEmail.ShouldBeNull();
        stored.IsEmailConfirmed.ShouldBeTrue();
        stored.TokenVersion.ShouldBe(1);
        (await TokensAsync(user, SecurityTokenType.EmailConfirmation)).ShouldAllBe(t => t.IsUsed);
        (await Security.ConfirmEmailChangeAsync(raw)).Success.ShouldBeFalse();
    }

    [Fact]
    public async Task ConfirmEmailChange_NewRequestReplacesTheOldLink()
    {
        var user = await Data.UserAsync();
        var first = $"{TestData.Unique("first")}@test.local";
        var second = $"{TestData.Unique("second")}@test.local";
        await Security.RequestEmailChangeAsync(user.ID, new ChangeEmailDto { NewEmail = first, CurrentPassword = TestData.Password });
        var firstRaw = CapturingEmailSender.LinkToken(await App.Mail.WaitForAsync(first, "Confirm"));
        await Security.RequestEmailChangeAsync(user.ID, new ChangeEmailDto { NewEmail = second, CurrentPassword = TestData.Password });

        (await Security.ConfirmEmailChangeAsync(firstRaw)).Success.ShouldBeFalse();
        (await ReloadAsync(user)).Email.ShouldBe(user.Email);
    }

    [Fact]
    public async Task ConfirmEmailChange_AddressTakenMeanwhile_IsRejected()
    {
        var user = await Data.UserAsync();
        var newEmail = $"{TestData.Unique("contested")}@test.local";
        await Security.RequestEmailChangeAsync(user.ID, new ChangeEmailDto { NewEmail = newEmail, CurrentPassword = TestData.Password });
        var raw = CapturingEmailSender.LinkToken(await App.Mail.WaitForAsync(newEmail, "Confirm"));
        await Data.UserAsync(u => u.Email = newEmail);

        var (success, message, _) = await Security.ConfirmEmailChangeAsync(raw);

        success.ShouldBeFalse();
        message.ShouldBe("This email address is already used by another account.");
        (await ReloadAsync(user)).Email.ShouldBe(user.Email);
    }

    [Fact]
    public async Task ConfirmEmailChange_ExpiredLink_IsRejected()
    {
        var user = await Data.UserAsync(u => u.PendingEmail = $"{TestData.Unique("late")}@test.local");
        var (_, raw) = await Data.SecurityTokenAsync(user, SecurityTokenType.EmailChange, expiresAt: DateTime.UtcNow.AddSeconds(-1));

        (await Security.ConfirmEmailChangeAsync(raw)).Success.ShouldBeFalse();
    }

    [Fact]
    public async Task ConfirmEmailChange_TwoConcurrentConfirmations_ApplyOnce()
    {
        var user = await Data.UserAsync(u => u.PendingEmail = $"{TestData.Unique("race")}@test.local");
        var (_, raw) = await Data.SecurityTokenAsync(user, SecurityTokenType.EmailChange);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Security.ConfirmEmailChangeAsync(raw)));

        results.Count(r => r.Success).ShouldBe(1);
        (await ReloadAsync(user)).TokenVersion.ShouldBe(1);
    }

    [Fact]
    public async Task CancelEmailChange_ClearsPendingAndKillsTheLink()
    {
        var user = await Data.UserAsync();
        var newEmail = $"{TestData.Unique("cancel")}@test.local";
        await Security.RequestEmailChangeAsync(user.ID, new ChangeEmailDto { NewEmail = newEmail, CurrentPassword = TestData.Password });
        var raw = CapturingEmailSender.LinkToken(await App.Mail.WaitForAsync(newEmail, "Confirm"));

        (await Security.CancelEmailChangeAsync(user.ID)).ShouldBeTrue();

        (await ReloadAsync(user)).PendingEmail.ShouldBeNull();
        (await Security.ConfirmEmailChangeAsync(raw)).Success.ShouldBeFalse();
        (await Security.CancelEmailChangeAsync(user.ID)).ShouldBeFalse();
    }

    /* Two-factor settings */

    [Fact]
    public async Task EnableTwoFactor_NeedsPasswordAndVerifiedEmail()
    {
        var unverified = await Data.UserAsync(u => u.IsEmailConfirmed = false);
        var verified = await Data.UserAsync();

        (await Security.RequestTwoFactorChangeAsync(verified.ID, true, "wrong")).Message.ShouldBe("Incorrect password.");
        (await Security.RequestTwoFactorChangeAsync(unverified.ID, true, TestData.Password)).Success.ShouldBeFalse();
        (await TokensAsync(unverified, SecurityTokenType.TwoFactorEnable)).ShouldBeEmpty();
    }

    [Fact]
    public async Task EnableTwoFactor_TwoStepsWithEmailedCode()
    {
        var user = await Data.UserAsync();

        (await Security.RequestTwoFactorChangeAsync(user.ID, true, TestData.Password)).Success.ShouldBeTrue();
        var code = CapturingEmailSender.Code(await App.Mail.WaitForAsync(user.Email, "enable two-factor"));
        var (success, _) = await Security.ConfirmTwoFactorChangeAsync(user.ID, true, code);

        success.ShouldBeTrue();
        var stored = await ReloadAsync(user);
        stored.IsTwoFactorEnabled.ShouldBeTrue();
        // Unlike a password or email change, this doesn't log anyone out
        stored.TokenVersion.ShouldBe(0);
        await App.Mail.WaitForAsync(user.Email, "Two-factor authentication enabled");
    }

    [Fact]
    public async Task EnableTwoFactor_AlreadyEnabled_IsRejected()
    {
        var user = await Data.UserAsync(u => u.IsTwoFactorEnabled = true);

        (await Security.RequestTwoFactorChangeAsync(user.ID, true, TestData.Password)).Message
            .ShouldBe("Two-factor authentication is already enabled.");
    }

    [Fact]
    public async Task DisableTwoFactor_WhenNotEnabled_IsRejected()
    {
        var user = await Data.UserAsync();

        (await Security.RequestTwoFactorChangeAsync(user.ID, false, TestData.Password)).Message
            .ShouldBe("Two-factor authentication is not enabled.");
    }

    [Fact]
    public async Task DisableTwoFactor_TwoStepsWithEmailedCode()
    {
        var user = await Data.UserAsync(u => u.IsTwoFactorEnabled = true);

        (await Security.RequestTwoFactorChangeAsync(user.ID, false, TestData.Password)).Success.ShouldBeTrue();
        var code = CapturingEmailSender.Code(await App.Mail.WaitForAsync(user.Email, "disable two-factor"));

        (await Security.ConfirmTwoFactorChangeAsync(user.ID, false, code)).Success.ShouldBeTrue();
        (await ReloadAsync(user)).IsTwoFactorEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task TwoFactorCodeRequest_HasACooldown()
    {
        var user = await Data.UserAsync();
        await Security.RequestTwoFactorChangeAsync(user.ID, true, TestData.Password);

        var (success, message) = await Security.RequestTwoFactorChangeAsync(user.ID, true, TestData.Password);

        success.ShouldBeFalse();
        message.ShouldBe("Please wait a minute before requesting another code.");
    }

    [Fact]
    public async Task TwoFactorCode_OfAnotherPurpose_DoesNotWork()
    {
        var user = await Data.UserAsync();
        var (_, code) = await Data.SecurityTokenAsync(user, SecurityTokenType.TwoFactorLogin);

        (await Security.ConfirmTwoFactorChangeAsync(user.ID, true, code)).Success.ShouldBeFalse();
    }

    [Fact]
    public async Task TwoFactorCode_FiveWrongTriesBurnIt()
    {
        var user = await Data.UserAsync();
        var (_, code) = await Data.SecurityTokenAsync(user, SecurityTokenType.TwoFactorEnable, raw: "111111");

        for (var i = 0; i < SecurityTokens.MaxCodeAttempts; i++)
            (await Security.ConfirmTwoFactorChangeAsync(user.ID, true, "222222")).Success.ShouldBeFalse();

        var token = (await TokensAsync(user, SecurityTokenType.TwoFactorEnable)).Single();
        token.FailedAttempts.ShouldBe(SecurityTokens.MaxCodeAttempts);
        token.IsUsed.ShouldBeTrue();
        (await Security.ConfirmTwoFactorChangeAsync(user.ID, true, code)).Success.ShouldBeFalse();
    }

    [Fact]
    public async Task TwoFactorCode_FourWrongTriesStillAllowTheRightOne()
    {
        var user = await Data.UserAsync();
        var (_, code) = await Data.SecurityTokenAsync(user, SecurityTokenType.TwoFactorEnable, raw: "111111");

        for (var i = 0; i < SecurityTokens.MaxCodeAttempts - 1; i++)
            await Security.ConfirmTwoFactorChangeAsync(user.ID, true, "222222");

        (await Security.ConfirmTwoFactorChangeAsync(user.ID, true, code)).Success.ShouldBeTrue();
    }

    [Fact]
    public async Task TwoFactorCode_Expired_IsRejected()
    {
        var user = await Data.UserAsync();
        var (_, code) = await Data.SecurityTokenAsync(user, SecurityTokenType.TwoFactorEnable, expiresAt: DateTime.UtcNow.AddSeconds(-1));

        (await Security.ConfirmTwoFactorChangeAsync(user.ID, true, code)).Success.ShouldBeFalse();
    }

    [Fact]
    public async Task TwoFactorCode_WrongTriesRacingTheRightOne_NeverReviveIt()
    {
        // A wrong try reads the token, then counts the failure; if the right code is used in between,
        // counting the failure must not mark the spent token unused again (which would allow a second use)
        for (var round = 0; round < 10; round++)
        {
            var user = await Data.UserAsync();
            var (_, code) = await Data.SecurityTokenAsync(user, SecurityTokenType.TwoFactorEnable, raw: "111111");

            var attempts = Enumerable.Range(0, 8)
                .Select(i => Security.ConfirmTwoFactorChangeAsync(user.ID, true, i == 0 ? code : "222222"));
            await Task.WhenAll(attempts);

            (await TokensAsync(user, SecurityTokenType.TwoFactorEnable)).Single().IsUsed.ShouldBeTrue($"round {round}");
        }
    }

    /* Two-factor login */

    private async Task<(User User, string Challenge, string Code)> StartLoginAsync()
    {
        var user = await Data.UserAsync(u => u.IsTwoFactorEnabled = true);
        var challenge = await Security.StartTwoFactorLoginAsync(user);
        var code = CapturingEmailSender.Code(await App.Mail.WaitForAsync(user.Email, "Your login code"));
        return (user, challenge, code);
    }

    [Fact]
    public async Task TwoFactorLogin_RightCodeCompletesOnce()
    {
        var (user, challenge, code) = await StartLoginAsync();

        var (success, _, loggedIn) = await Security.CompleteTwoFactorLoginAsync(challenge, code);

        success.ShouldBeTrue();
        loggedIn!.ID.ShouldBe(user.ID);
        (await Security.CompleteTwoFactorLoginAsync(challenge, code)).Success.ShouldBeFalse();
    }

    [Fact]
    public async Task TwoFactorLogin_WrongCode_IsRejected()
    {
        var (_, challenge, code) = await StartLoginAsync();
        var wrong = code == "000000" ? "000001" : "000000";

        var (success, message, _) = await Security.CompleteTwoFactorLoginAsync(challenge, wrong);

        success.ShouldBeFalse();
        message.ShouldBe("The code is invalid or has expired.");
    }

    [Fact]
    public async Task TwoFactorLogin_LogoutAllOrPasswordChangeKillsThePendingLogin()
    {
        var (user, challenge, code) = await StartLoginAsync();
        await Query().Users.Where(u => u.ID == user.ID).ExecuteUpdateAsync(s => s.SetProperty(u => u.TokenVersion, u => u.TokenVersion + 1));

        var (success, message, _) = await Security.CompleteTwoFactorLoginAsync(challenge, code);

        success.ShouldBeFalse();
        message.ShouldBe("Your login attempt has expired. Please log in again.");
    }

    [Fact]
    public async Task TwoFactorLogin_DisablingTwoFactorKillsThePendingLogin()
    {
        var (user, challenge, code) = await StartLoginAsync();
        await Query().Users.Where(u => u.ID == user.ID).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsTwoFactorEnabled, false));

        (await Security.CompleteTwoFactorLoginAsync(challenge, code)).Success.ShouldBeFalse();
    }

    [Fact]
    public async Task TwoFactorLogin_ForgedChallenge_IsRejected()
    {
        var (_, _, code) = await StartLoginAsync();

        (await Security.CompleteTwoFactorLoginAsync("forged.challenge.token", code)).Success.ShouldBeFalse();
    }

    [Fact]
    public async Task TwoFactorLoginResend_HasCooldownThenReplacesTheCode()
    {
        var (user, challenge, oldCode) = await StartLoginAsync();

        (await Security.ResendTwoFactorLoginCodeAsync(challenge)).Success.ShouldBeFalse();

        await AgeTokensAsync(user, SecurityTokenType.TwoFactorLogin);
        (await Security.ResendTwoFactorLoginCodeAsync(challenge)).Success.ShouldBeTrue();

        await TestWait.UntilAsync(() => App.Mail.Inbox(user.Email).Count(m => m.Subject == "Your login code") == 2);
        var newCode = CapturingEmailSender.Code(App.Mail.Inbox(user.Email).Last(m => m.Subject == "Your login code"));
        if (newCode != oldCode)
            (await Security.CompleteTwoFactorLoginAsync(challenge, oldCode)).Success.ShouldBeFalse();
        (await Security.CompleteTwoFactorLoginAsync(challenge, newCode)).Success.ShouldBeTrue();
    }

    [Fact]
    public async Task GetStatus_ReflectsTheAccount()
    {
        var user = await Data.UserAsync(u => { u.IsTwoFactorEnabled = true; u.PendingEmail = "pending@test.local"; });

        var status = await Security.GetStatusAsync(user.ID);

        status!.Email.ShouldBe(user.Email);
        status.IsEmailConfirmed.ShouldBeTrue();
        status.IsTwoFactorEnabled.ShouldBeTrue();
        status.PendingEmail.ShouldBe("pending@test.local");
        (await Security.GetStatusAsync(-1)).ShouldBeNull();
    }
}
