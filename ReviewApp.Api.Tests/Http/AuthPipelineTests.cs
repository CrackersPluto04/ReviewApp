using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.Services;
using ReviewApp.Api.Tests.Support;

namespace ReviewApp.Api.Tests.Http;

// Login, the session cookie and its revocation, through the real pipeline
public class AuthPipelineTests : HttpTest
{
    private const string CheckAuth = "api/Auth/check-auth";

    private static HttpClient WithToken(HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Add("Cookie", $"{TokenService.CookieName}={token}");
        return client;
    }

    /* Registration */

    [Fact]
    public async Task Register_CreatesAnUnverifiedUserWithFavouritesAndMailsTheLink()
    {
        var username = TestData.Unique();
        var email = $"{username}@test.local";

        var response = await PostJsonAsync(Anonymous(), "api/Auth/register", new { username = $" {username} ", email, password = "Passw0rd!" });

        await ExpectAsync(response, HttpStatusCode.OK);
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();
        var user = await Query().Users.SingleAsync(u => u.Email == email);
        user.Username.ShouldBe(username);
        user.IsEmailConfirmed.ShouldBeFalse();
        user.PasswordHash.ShouldStartWith("$2");
        (await Data.FavouritesAsync(user)).ShouldNotBeNull();
        await App.Mail.WaitForAsync(email, "Welcome to THE Review App");
    }

    [Fact]
    public async Task Register_TakenUsernameOrEmail_IsA400WithMessage()
    {
        var existing = await Data.UserAsync();

        (await ErrorAsync(await PostJsonAsync(Anonymous(), "api/Auth/register",
            new { username = existing.Username.ToUpperInvariant(), email = $"{TestData.Unique()}@test.local", password = "Passw0rd!" }), HttpStatusCode.BadRequest))
            .ShouldBe("Username already exists.");
        (await ErrorAsync(await PostJsonAsync(Anonymous(), "api/Auth/register",
            new { username = TestData.Unique(), email = existing.Email, password = "Passw0rd!" }), HttpStatusCode.BadRequest))
            .ShouldBe("User with this email already exists.");
    }

    [Fact]
    public async Task Register_InvalidInput_IsAValidationProblem()
    {
        var body = await ExpectAsync(await PostJsonAsync(Anonymous(), "api/Auth/register",
            new { username = "ab", email = "not-an-email", password = "short" }), HttpStatusCode.BadRequest);

        var errors = body.GetProperty("errors");
        errors.TryGetProperty("Username", out _).ShouldBeTrue();
        errors.TryGetProperty("Email", out _).ShouldBeTrue();
        errors.TryGetProperty("Password", out _).ShouldBeTrue();
    }

    /* Login and the cookie */

    [Fact]
    public async Task Login_SetsTheHardenedSessionCookieAndReturnsTheUser()
    {
        var user = await Data.UserAsync();

        var response = await LoginAsync(Browser(), user);

        var body = await ExpectAsync(response, HttpStatusCode.OK);
        var cookie = response.Headers.GetValues("Set-Cookie").Single().ToLowerInvariant();
        cookie.ShouldStartWith(TokenService.CookieName.ToLowerInvariant() + "=");
        cookie.ShouldContain("httponly");
        cookie.ShouldContain("secure");
        cookie.ShouldContain("samesite=strict");
        cookie.ShouldContain("path=/");
        var authUser = body.GetProperty("user");
        authUser.Int("id").ShouldBe(user.ID);
        authUser.Str("username").ShouldBe(user.Username);
        authUser.Str("tier").ShouldBe("Free");
        authUser.Bool("isPremium").ShouldBeFalse();
        authUser.GetProperty("entitlements").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Login_CookieAuthenticatesLaterRequests()
    {
        var user = await Data.UserAsync();
        var browser = Browser();
        await LoginAsync(browser, user);

        var body = await ExpectAsync(await browser.GetAsync(CheckAuth), HttpStatusCode.OK);

        body.Int("id").ShouldBe(user.ID);
        body.Bool("isEmailConfirmed").ShouldBeTrue();
    }

    [Fact]
    public async Task Login_WrongPasswordAndUnknownEmail_LookTheSame()
    {
        var user = await Data.UserAsync();

        var wrongPassword = await ErrorAsync(await LoginAsync(Browser(), user, "Wrong-pass1"), HttpStatusCode.Unauthorized);
        var unknown = await ErrorAsync(await PostJsonAsync(Anonymous(), "api/Auth/login",
            new { email = $"{TestData.Unique("ghost")}@test.local", password = TestData.Password }), HttpStatusCode.Unauthorized);

        wrongPassword.ShouldBe("Invalid email or password.");
        unknown.ShouldBe(wrongPassword);
    }

    [Fact]
    public async Task CheckAuth_PremiumUserCarriesTierAndEntitlements()
    {
        var user = await Data.UserAsync();
        await Data.PremiumAsync(user);

        var body = await ExpectAsync(await As(user).GetAsync(CheckAuth), HttpStatusCode.OK);

        body.Str("tier").ShouldBe("Premium");
        body.Bool("isPremium").ShouldBeTrue();
        body.GetProperty("entitlements").EnumerateArray().Select(e => e.GetString()).ShouldContain(FeatureKeys.ProfileBadge);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutCookie_Is401()
    {
        (await Anonymous().GetAsync(CheckAuth)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_DeletesTheCookie()
    {
        var user = await Data.UserAsync();
        var browser = Browser();
        await LoginAsync(browser, user);

        var response = await browser.PostAsync("api/Auth/logout", null);

        await ExpectAsync(response, HttpStatusCode.OK);
        response.Headers.GetValues("Set-Cookie").Single().ShouldContain("expires=Thu, 01 Jan 1970");
        (await browser.GetAsync(CheckAuth)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /* Rejected tokens */

    [Fact]
    public async Task Token_WithAnOldVersion_IsRejected()
    {
        var user = await Data.UserAsync(u => u.TokenVersion = 3);

        (await WithToken(Anonymous(), TestTokens.Session(user.ID, tokenVersion: 2)).GetAsync(CheckAuth)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await WithToken(Anonymous(), TestTokens.Session(user.ID, tokenVersion: 3)).GetAsync(CheckAuth)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Token_WithoutVersionClaim_CountsAsVersionZero()
    {
        var user = await Data.UserAsync();

        var token = TestTokens.Create([new Claim("id", user.ID.ToString())]);

        (await WithToken(Anonymous(), token).GetAsync(CheckAuth)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Token_ExpiredForeignKeyWrongAudienceOrUnknownUser_IsRejected()
    {
        var user = await Data.UserAsync();
        var claims = new[] { new Claim("id", user.ID.ToString()), new Claim("ver", "0") };

        var expired = TestTokens.Session(user.ID, expires: DateTime.UtcNow.AddMinutes(-1));
        var foreignKey = TestTokens.Create(claims, key: "some-other-signing-key-that-is-long-enough");
        var wrongAudience = TestTokens.Create(claims, audience: "someone-else");
        var unknownUser = TestTokens.Session(-1);

        foreach (var token in new[] { expired, foreignKey, wrongAudience, unknownUser, "garbage" })
            (await WithToken(Anonymous(), token).GetAsync(CheckAuth)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, token);
    }

    [Fact]
    public async Task Token_InTheAuthorizationHeader_AlsoWorks()
    {
        var user = await Data.UserAsync();
        var client = Anonymous();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Session(user.ID));

        (await client.GetAsync(CheckAuth)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /* Revocation */

    [Fact]
    public async Task LogoutAll_RevokesEverySession()
    {
        var user = await Data.UserAsync();
        var laptop = Browser();
        var phone = Browser();
        await LoginAsync(laptop, user);
        await LoginAsync(phone, user);

        await ExpectAsync(await laptop.PostAsync("api/Auth/logout-all", null), HttpStatusCode.OK);

        (await laptop.GetAsync(CheckAuth)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await phone.GetAsync(CheckAuth)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Query().Users.SingleAsync(u => u.ID == user.ID)).TokenVersion.ShouldBe(1);
    }

    [Fact]
    public async Task PasswordChange_KeepsThisBrowserAndLogsOutTheOthers()
    {
        var user = await Data.UserAsync();
        var current = Browser();
        var other = Browser();
        await LoginAsync(current, user);
        await LoginAsync(other, user);

        var response = await current.PutAsJsonAsync("api/User/me/password", new { currentPassword = TestData.Password, newPassword = "N3w-Password" });

        await ExpectAsync(response, HttpStatusCode.OK);
        (await current.GetAsync(CheckAuth)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await other.GetAsync(CheckAuth)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        await ExpectAsync(await LoginAsync(Browser(), user, "N3w-Password"), HttpStatusCode.OK);
    }

    [Fact]
    public async Task ConfirmedEmailChange_LogsOutOtherSessionsButNotTheConfirmingBrowser()
    {
        var user = await Data.UserAsync();
        var confirming = Browser();
        var other = Browser();
        await LoginAsync(confirming, user);
        await LoginAsync(other, user);
        var newEmail = $"{TestData.Unique("moved")}@test.local";
        await ExpectAsync(await confirming.PutAsJsonAsync("api/User/me/email", new { newEmail, currentPassword = TestData.Password }), HttpStatusCode.OK);
        var token = CapturingEmailSender.LinkToken(await App.Mail.WaitForAsync(newEmail, "Confirm your new email address"));

        var body = await ExpectAsync(await PostJsonAsync(confirming, "api/Security/confirm-email-change", new { token }), HttpStatusCode.OK);

        body.Bool("loggedIn").ShouldBeTrue();
        (await confirming.GetAsync(CheckAuth)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await other.GetAsync(CheckAuth)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /* Two-factor login */

    [Fact]
    public async Task TwoFactorLogin_IssuesTheCookieOnlyAfterTheCode()
    {
        var user = await Data.UserAsync(u => u.IsTwoFactorEnabled = true);
        var browser = Browser();

        var first = await LoginAsync(browser, user);
        var body = await ExpectAsync(first, HttpStatusCode.OK);

        body.Bool("twoFactorRequired").ShouldBeTrue();
        first.Headers.Contains("Set-Cookie").ShouldBeFalse();
        (await browser.GetAsync(CheckAuth)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var code = CapturingEmailSender.Code(await App.Mail.WaitForAsync(user.Email, "Your login code"));
        var second = await PostJsonAsync(browser, "api/Auth/login/2fa", new { challengeToken = body.Str("challengeToken"), code });

        (await ExpectAsync(second, HttpStatusCode.OK)).GetProperty("user").Int("id").ShouldBe(user.ID);
        (await browser.GetAsync(CheckAuth)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TwoFactorChallenge_IsNeverAcceptedAsASession()
    {
        var user = await Data.UserAsync(u => u.IsTwoFactorEnabled = true);
        var challenge = (await ExpectAsync(await LoginAsync(Browser(), user), HttpStatusCode.OK)).Str("challengeToken")!;
        var bearer = Anonymous();
        bearer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", challenge);

        (await WithToken(Anonymous(), challenge).GetAsync(CheckAuth)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await bearer.GetAsync(CheckAuth)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TwoFactorLogin_WrongCode_Is401()
    {
        var user = await Data.UserAsync(u => u.IsTwoFactorEnabled = true);
        var challenge = (await ExpectAsync(await LoginAsync(Browser(), user), HttpStatusCode.OK)).Str("challengeToken");
        var code = CapturingEmailSender.Code(await App.Mail.WaitForAsync(user.Email, "Your login code"));
        var wrong = code == "000000" ? "000001" : "000000";

        (await ErrorAsync(await PostJsonAsync(Browser(), "api/Auth/login/2fa", new { challengeToken = challenge, code = wrong }), HttpStatusCode.Unauthorized))
            .ShouldBe("The code is invalid or has expired.");
    }

    [Fact]
    public async Task TwoFactorLogin_WrongPassword_NeverRevealsThatTwoFactorIsOn()
    {
        var user = await Data.UserAsync(u => u.IsTwoFactorEnabled = true);

        (await ErrorAsync(await LoginAsync(Browser(), user, "Wrong-pass1"), HttpStatusCode.Unauthorized)).ShouldBe("Invalid email or password.");
        (await App.Mail.SettledInboxAsync(user.Email)).ShouldBeEmpty();
    }
}
