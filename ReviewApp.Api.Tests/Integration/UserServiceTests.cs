using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services.Interfaces;
using ReviewApp.Api.Tests.Support;

namespace ReviewApp.Api.Tests.Integration;

public class UserServiceTests : IntegrationTest
{
    private IUserService Users => Service<IUserService>();

    [Fact]
    public async Task Search_FindsBySubstringAndCapsAtTen()
    {
        var token = TestData.Unique("srch");
        for (var i = 0; i < 12; i++)
            await Data.UserAsync(u => u.Username = $"{token}x{i}");

        var result = await Users.SearchUsersAsync(token);

        result.Count.ShouldBe(10);
        result.ShouldAllBe(u => u.Username.StartsWith(token));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Search_BlankQuery_ReturnsNothing(string query)
    {
        (await Users.SearchUsersAsync(query)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Profile_UnknownUser_IsNull()
    {
        (await Users.GetUserProfileAsync(TestData.Unique("nobody"), null)).ShouldBeNull();
    }

    [Fact]
    public async Task Profile_HasCountsAndFollowStateForTheViewer()
    {
        var owner = await Data.UserAsync(u => u.Bio = "Hello");
        var follower = await Data.UserAsync();
        var stranger = await Data.UserAsync();
        await Data.FollowAsync(follower, owner);
        await Data.FollowAsync(owner, stranger);

        var asFollower = await Users.GetUserProfileAsync(owner.Username, follower.ID);
        var asStranger = await Users.GetUserProfileAsync(owner.Username, stranger.ID);
        var asOwner = await Users.GetUserProfileAsync(owner.Username, owner.ID);
        var anonymous = await Users.GetUserProfileAsync(owner.Username, null);

        asFollower!.FollowersCount.ShouldBe(1);
        asFollower.FollowingCount.ShouldBe(1);
        asFollower.Bio.ShouldBe("Hello");
        asFollower.CreatedAt.ShouldBe(DateTime.UtcNow.ToString("yyyy-MM-dd"));
        asFollower.IsFollowedByCurrentUser.ShouldBeTrue();
        asStranger!.IsFollowedByCurrentUser.ShouldBeFalse();
        asOwner!.IsFollowedByCurrentUser.ShouldBeFalse();
        anonymous!.IsFollowedByCurrentUser.ShouldBeFalse();
    }

    [Fact]
    public async Task Profile_ShowsPremiumBadgeOnlyWhileEntitled()
    {
        var premium = await Data.UserAsync();
        var lapsed = await Data.UserAsync();
        var free = await Data.UserAsync();
        await Data.PremiumAsync(premium);
        await Data.SubscriptionAsync(lapsed, SubscriptionStatus.Active, DateTime.UtcNow.AddDays(-3));

        (await Users.GetUserProfileAsync(premium.Username, null))!.IsPremium.ShouldBeTrue();
        (await Users.GetUserProfileAsync(lapsed.Username, null))!.IsPremium.ShouldBeFalse();
        (await Users.GetUserProfileAsync(free.Username, null))!.IsPremium.ShouldBeFalse();
    }

    [Fact]
    public async Task Update_RenamesTrimmedUsernameAndKeepsUnsentFields()
    {
        var user = await Data.UserAsync(u => { u.Bio = "Old bio"; u.ProfilePictureUrl = "https://img.test/a.png"; });
        var newName = TestData.Unique("renamed");

        var (success, _) = await Users.UpdateUserProfileAsync(user.ID, new UserUpdateDto { Username = $"  {newName} " });

        success.ShouldBeTrue();
        var stored = await Query().Users.SingleAsync(u => u.ID == user.ID);
        stored.Username.ShouldBe(newName);
        stored.Bio.ShouldBe("Old bio");
        stored.ProfilePictureUrl.ShouldBe("https://img.test/a.png");
    }

    [Fact]
    public async Task Update_EmptyBioClearsIt()
    {
        var user = await Data.UserAsync(u => u.Bio = "Old bio");

        await Users.UpdateUserProfileAsync(user.ID, new UserUpdateDto { Bio = "" });

        (await Query().Users.SingleAsync(u => u.ID == user.ID)).Bio.ShouldBe("");
    }

    [Fact]
    public async Task Update_UsernameTakenInAnyCase_IsRejected()
    {
        var existing = await Data.UserAsync();
        var user = await Data.UserAsync();

        var (success, message) = await Users.UpdateUserProfileAsync(user.ID, new UserUpdateDto { Username = existing.Username.ToUpperInvariant() });

        success.ShouldBeFalse();
        message.ShouldBe("Username already taken.");
    }

    [Fact]
    public async Task Update_CaseOnlyChangeOfOwnName_IsAllowed()
    {
        var user = await Data.UserAsync();

        var (success, _) = await Users.UpdateUserProfileAsync(user.ID, new UserUpdateDto { Username = user.Username.ToUpperInvariant() });

        success.ShouldBeTrue();
        (await Query().Users.SingleAsync(u => u.ID == user.ID)).Username.ShouldBe(user.Username.ToUpperInvariant());
    }

    [Fact]
    public async Task Update_TooShortAfterTrim_IsRejected()
    {
        var user = await Data.UserAsync();

        var (success, _) = await Users.UpdateUserProfileAsync(user.ID, new UserUpdateDto { Username = "  ab  " });

        success.ShouldBeFalse();
    }

    [Fact]
    public async Task ChangePassword_WrongCurrent_IsRejectedAndChangesNothing()
    {
        var user = await Data.UserAsync();

        var (success, message, _) = await Users.ChangePasswordAsync(user.ID,
            new ChangePasswordDto { CurrentPassword = "wrong-password", NewPassword = "NewPassw0rd" });

        success.ShouldBeFalse();
        message.ShouldBe("Incorrect password.");
        (await Query().Users.SingleAsync(u => u.ID == user.ID)).TokenVersion.ShouldBe(0);
    }

    [Fact]
    public async Task ChangePassword_SameAsCurrent_IsRejected()
    {
        var user = await Data.UserAsync();

        var (success, _, _) = await Users.ChangePasswordAsync(user.ID,
            new ChangePasswordDto { CurrentPassword = TestData.Password, NewPassword = TestData.Password });

        success.ShouldBeFalse();
    }

    [Fact]
    public async Task ChangePassword_StoresNewHashBumpsVersionAndNotifies()
    {
        var user = await Data.UserAsync();

        var (success, _, updated) = await Users.ChangePasswordAsync(user.ID,
            new ChangePasswordDto { CurrentPassword = TestData.Password, NewPassword = "NewPassw0rd" });

        success.ShouldBeTrue();
        updated!.TokenVersion.ShouldBe(1);
        var stored = await Query().Users.SingleAsync(u => u.ID == user.ID);
        stored.TokenVersion.ShouldBe(1);
        BCrypt.Net.BCrypt.Verify("NewPassw0rd", stored.PasswordHash).ShouldBeTrue();
        await App.Mail.WaitForAsync(user.Email, "Your password was changed");
        (await Query().Notifications.AnyAsync(n => n.RecipientUserID == user.ID && n.Title == "Password changed")).ShouldBeTrue();
    }

    [Fact]
    public async Task FollowerLists_ShowWhetherTheViewerFollowsEachEntry()
    {
        var target = await Data.UserAsync();
        var viewer = await Data.UserAsync();
        var a = await Data.UserAsync();
        var b = await Data.UserAsync();
        await Data.FollowAsync(a, target);
        await Data.FollowAsync(b, target);
        await Data.FollowAsync(target, a);
        await Data.FollowAsync(viewer, a);

        var followers = await Users.GetUserFollowersAsync(target.Username, viewer.ID);
        var following = await Users.GetUserFollowingAsync(target.Username, viewer.ID);

        followers.Select(f => f.Username).ShouldBe([a.Username, b.Username], ignoreOrder: true);
        followers.Single(f => f.ID == a.ID).IsFollowedByCurrentUser.ShouldBe(true);
        followers.Single(f => f.ID == b.ID).IsFollowedByCurrentUser.ShouldBe(false);
        following.Single().ID.ShouldBe(a.ID);
    }

    [Fact]
    public async Task FollowerLists_UnknownUser_AreEmpty()
    {
        var name = TestData.Unique("nobody");

        (await Users.GetUserFollowersAsync(name, null)).ShouldBeEmpty();
        (await Users.GetUserFollowingAsync(name, null)).ShouldBeEmpty();
    }
}
