using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services.Interfaces;
using ReviewApp.Api.Tests.Support;

namespace ReviewApp.Api.Tests.Integration;

public class FollowerServiceTests : IntegrationTest
{
    private IFollowerService Followers => Service<IFollowerService>();

    private Task<bool> FollowsAsync(User follower, User following) =>
        Query().UserFollowers.AnyAsync(f => f.FollowerID == follower.ID && f.FollowingID == following.ID);

    [Fact]
    public async Task Follow_StoresTheFollowAndNotifiesTheTarget()
    {
        var follower = await Data.UserAsync();
        var target = await Data.UserAsync();

        (await Followers.FollowUserAsync(follower.ID, target.ID)).Success.ShouldBeTrue();

        (await FollowsAsync(follower, target)).ShouldBeTrue();
        var notification = await Query().Notifications.SingleAsync(n => n.RecipientUserID == target.ID);
        notification.Type.ShouldBe(NotificationType.NewFollower);
        notification.ActorUserID.ShouldBe(follower.ID);
        notification.Message.ShouldBe($"{follower.Username} started following you.");
    }

    [Fact]
    public async Task Follow_Back_SaysSoInTheNotification()
    {
        var a = await Data.UserAsync();
        var b = await Data.UserAsync();
        await Data.FollowAsync(a, b);

        await Followers.FollowUserAsync(b.ID, a.ID);

        (await Query().Notifications.SingleAsync(n => n.RecipientUserID == a.ID)).Message.ShouldBe($"{b.Username} followed you back.");
    }

    [Fact]
    public async Task Follow_Yourself_IsRejected()
    {
        var user = await Data.UserAsync();

        (await Followers.FollowUserAsync(user.ID, user.ID)).Message.ShouldBe("You cannot follow yourself.");
    }

    [Fact]
    public async Task Follow_UnknownUser_IsRejected()
    {
        var user = await Data.UserAsync();

        (await Followers.FollowUserAsync(user.ID, -1)).Success.ShouldBeFalse();
    }

    [Fact]
    public async Task Follow_Twice_IsRejected()
    {
        var follower = await Data.UserAsync();
        var target = await Data.UserAsync();
        await Followers.FollowUserAsync(follower.ID, target.ID);

        (await Followers.FollowUserAsync(follower.ID, target.ID)).Message.ShouldBe("You already follow this user.");
    }

    [Fact(Skip = "Known issue F6 (FINDINGS.md): a parallel duplicate follow hits the primary key and throws (a 500) instead of 'already following'")]
    public async Task Follow_ConcurrentDuplicates_OneSucceedsNoneThrows()
    {
        var follower = await Data.UserAsync();
        var target = await Data.UserAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Service<IFollowerService>().FollowUserAsync(follower.ID, target.ID)));

        results.Count(r => r.Success).ShouldBe(1);
    }

    [Fact]
    public async Task RepeatedFollow_NotifiesOncePerWeek()
    {
        var follower = await Data.UserAsync();
        var target = await Data.UserAsync();

        await Followers.FollowUserAsync(follower.ID, target.ID);
        await Followers.UnfollowUserAsync(follower.ID, target.ID);
        await Followers.FollowUserAsync(follower.ID, target.ID);

        (await Query().Notifications.CountAsync(n => n.RecipientUserID == target.ID)).ShouldBe(1);
    }

    [Fact]
    public async Task RepeatedFollow_AfterAWeek_NotifiesAgain()
    {
        var follower = await Data.UserAsync();
        var target = await Data.UserAsync();
        await Data.NotificationAsync(target, NotificationType.NewFollower, createdAt: DateTime.UtcNow.AddDays(-8), actor: follower);

        await Followers.FollowUserAsync(follower.ID, target.ID);

        (await Query().Notifications.CountAsync(n => n.RecipientUserID == target.ID)).ShouldBe(2);
    }

    [Fact]
    public async Task Unfollow_RemovesOnlyAnExistingFollow()
    {
        var follower = await Data.UserAsync();
        var target = await Data.UserAsync();
        await Data.FollowAsync(follower, target);

        (await Followers.UnfollowUserAsync(follower.ID, target.ID)).Success.ShouldBeTrue();
        (await FollowsAsync(follower, target)).ShouldBeFalse();
        (await Followers.UnfollowUserAsync(follower.ID, target.ID)).Message.ShouldBe("You are not following this user.");
    }

    [Fact]
    public async Task RemoveFollower_RemovesTheirFollowNotYours()
    {
        var me = await Data.UserAsync();
        var fan = await Data.UserAsync();
        await Data.FollowAsync(fan, me);
        await Data.FollowAsync(me, fan);

        (await Followers.RemoveFollowerAsync(me.ID, fan.ID)).Success.ShouldBeTrue();

        (await FollowsAsync(fan, me)).ShouldBeFalse();
        (await FollowsAsync(me, fan)).ShouldBeTrue();
        (await Followers.RemoveFollowerAsync(me.ID, fan.ID)).Message.ShouldBe("This user is not following you.");
    }

    [Fact]
    public async Task UserWithFollows_CantBeDeletedDirectly()
    {
        // Restrict on both sides avoids multiple cascade paths on Users; deleting an account must clean follows first
        var follower = await Data.UserAsync();
        var target = await Data.UserAsync();
        await Data.FollowAsync(follower, target);

        // ExecuteDelete bypasses the change tracker, so the FK violation surfaces as the raw SqlException
        await Should.ThrowAsync<Microsoft.Data.SqlClient.SqlException>(() => Query().Users.Where(u => u.ID == target.ID).ExecuteDeleteAsync());
    }
}
