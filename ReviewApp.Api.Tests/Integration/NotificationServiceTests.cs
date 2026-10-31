using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services;
using ReviewApp.Api.Services.Interfaces;
using ReviewApp.Api.Tests.Support;

namespace ReviewApp.Api.Tests.Integration;

public class NotificationServiceTests : IntegrationTest
{
    private INotificationService Notifications => Service<INotificationService>();

    private Task<List<Notification>> StoredAsync(User user) =>
        Query().Notifications.Where(n => n.RecipientUserID == user.ID).OrderBy(n => n.ID).ToListAsync();

    /* Creating */

    [Fact]
    public async Task Create_StoresReadyToDisplayText()
    {
        var user = await Data.UserAsync();

        await Notifications.CreateSystemAsync(user.ID, "Hello", "World");

        var stored = (await StoredAsync(user)).Single();
        stored.Type.ShouldBe(NotificationType.System);
        stored.Title.ShouldBe("Hello");
        stored.Message.ShouldBe("World");
        stored.IsSeen.ShouldBeFalse();
    }

    [Fact]
    public async Task Create_TooLongText_IsCutWithAnEllipsis()
    {
        var user = await Data.UserAsync();

        await Notifications.CreateSystemAsync(user.ID, new string('t', 150), new string('m', 600));

        var stored = (await StoredAsync(user)).Single();
        stored.Title.Length.ShouldBe(Notification.MaxTitleLength);
        stored.Title.ShouldEndWith("…");
        stored.Message.Length.ShouldBe(Notification.MaxMessageLength);
        stored.Message.ShouldEndWith("…");
    }

    [Fact]
    public async Task Create_ForAMissingRecipient_NeverThrows()
    {
        await Should.NotThrowAsync(() => Notifications.CreateSystemAsync(-1, "Title", "Message"));
    }

    [Fact]
    public async Task Create_OverTheCap_DropsTheOldestSeenFirst()
    {
        var user = await Data.UserAsync();
        var seen = await Data.NotificationsAsync(user, 5, seen: true);
        await Data.NotificationsAsync(user, NotificationService.MaxPerUser - 5);

        await Notifications.CreateSystemAsync(user.ID, "New", "One");

        var stored = await StoredAsync(user);
        stored.Count.ShouldBe(NotificationService.MaxPerUser);
        stored.ShouldNotContain(n => n.ID == seen[0].ID);
        stored.Count(n => n.IsSeen).ShouldBe(4);
        stored.Last().Title.ShouldBe("New");
    }

    [Fact]
    public async Task Create_OverTheCapWithoutSeenOnes_DropsTheOldestUnseen()
    {
        var user = await Data.UserAsync();
        var existing = await Data.NotificationsAsync(user, NotificationService.MaxPerUser);

        await Notifications.CreateSystemAsync(user.ID, "New", "One");

        var stored = await StoredAsync(user);
        stored.Count.ShouldBe(NotificationService.MaxPerUser);
        stored.First().ID.ShouldBe(existing[1].ID);
    }

    /* Listing */

    [Fact]
    public async Task List_NewestFirstWithKeysetPaging()
    {
        var user = await Data.UserAsync();
        var created = await Data.NotificationsAsync(user, 5);

        var first = await Notifications.GetNotificationsAsync(user.ID, new NotificationQueryParams { PageSize = 2 });
        var second = await Notifications.GetNotificationsAsync(user.ID, new NotificationQueryParams { PageSize = 2, AfterId = first.Items.Last().ID });
        var third = await Notifications.GetNotificationsAsync(user.ID, new NotificationQueryParams { PageSize = 2, AfterId = second.Items.Last().ID });

        first.Items.Select(n => n.ID).ShouldBe([created[4].ID, created[3].ID]);
        first.HasMore.ShouldBeTrue();
        second.Items.Select(n => n.ID).ShouldBe([created[2].ID, created[1].ID]);
        third.Items.Single().ID.ShouldBe(created[0].ID);
        third.HasMore.ShouldBeFalse();
    }

    [Fact]
    public async Task List_OldestFirst()
    {
        var user = await Data.UserAsync();
        var created = await Data.NotificationsAsync(user, 3);

        var page = await Notifications.GetNotificationsAsync(user.ID, new NotificationQueryParams { Sort = "oldest", PageSize = 2 });
        var next = await Notifications.GetNotificationsAsync(user.ID, new NotificationQueryParams { Sort = "oldest", AfterId = page.Items.Last().ID });

        page.Items.Select(n => n.ID).ShouldBe([created[0].ID, created[1].ID]);
        next.Items.Single().ID.ShouldBe(created[2].ID);
    }

    [Fact]
    public async Task List_StatusFilterWithCounts()
    {
        var user = await Data.UserAsync();
        await Data.NotificationsAsync(user, 2, seen: true);
        await Data.NotificationsAsync(user, 3);

        var seen = await Notifications.GetNotificationsAsync(user.ID, new NotificationQueryParams { Status = "seen" });
        var unseen = await Notifications.GetNotificationsAsync(user.ID, new NotificationQueryParams { Status = "unseen" });
        var all = await Notifications.GetNotificationsAsync(user.ID, new NotificationQueryParams());

        seen.Items.ShouldAllBe(n => n.IsSeen);
        seen.TotalCount.ShouldBe(2);
        unseen.TotalCount.ShouldBe(3);
        all.TotalCount.ShouldBe(5);
        new[] { seen, unseen, all }.ShouldAllBe(page => page.UnseenCount == 3);
    }

    [Fact]
    public async Task List_IncludesActorTargetAndUtcDate()
    {
        var user = await Data.UserAsync();
        var actor = await Data.UserAsync(u => u.ProfilePictureUrl = "https://img.test/actor.png");
        await Notifications.CreateAsync(new Notification
        {
            RecipientUserID = user.ID, ActorUserID = actor.ID, Type = NotificationType.ReviewReply,
            ReviewID = 12, ReplyID = 34, Title = "Reply", Message = "Hi"
        });

        var item = (await Notifications.GetNotificationsAsync(user.ID, new NotificationQueryParams())).Items.Single();

        item.Type.ShouldBe("ReviewReply");
        item.ActorUsername.ShouldBe(actor.Username);
        item.ActorProfilePictureUrl.ShouldBe("https://img.test/actor.png");
        item.ReviewID.ShouldBe(12);
        item.ReplyID.ShouldBe(34);
        item.CreatedAt.Kind.ShouldBe(DateTimeKind.Utc);
    }

    [Fact]
    public async Task List_OnlyTheUsersOwn()
    {
        var user = await Data.UserAsync();
        await Data.NotificationsAsync(await Data.UserAsync(), 3);

        var page = await Notifications.GetNotificationsAsync(user.ID, new NotificationQueryParams());

        page.Items.ShouldBeEmpty();
        page.TotalCount.ShouldBe(0);
    }

    /* Changing */

    [Fact]
    public async Task SetSeen_TogglesOwnOnly()
    {
        var user = await Data.UserAsync();
        var mine = await Data.NotificationAsync(user);
        var theirs = await Data.NotificationAsync(await Data.UserAsync());

        (await Notifications.SetSeenAsync(user.ID, mine.ID, true)).ShouldBeTrue();
        (await Notifications.GetUnseenCountAsync(user.ID)).ShouldBe(0);
        (await Notifications.SetSeenAsync(user.ID, mine.ID, false)).ShouldBeTrue();
        (await Notifications.GetUnseenCountAsync(user.ID)).ShouldBe(1);
        (await Notifications.SetSeenAsync(user.ID, theirs.ID, true)).ShouldBeFalse();
        (await Query().Notifications.SingleAsync(n => n.ID == theirs.ID)).IsSeen.ShouldBeFalse();
    }

    [Fact]
    public async Task MarkAllSeen_ReturnsHowManyChanged()
    {
        var user = await Data.UserAsync();
        await Data.NotificationsAsync(user, 3);
        await Data.NotificationsAsync(user, 1, seen: true);

        (await Notifications.MarkAllSeenAsync(user.ID)).ShouldBe(3);
        (await Notifications.GetUnseenCountAsync(user.ID)).ShouldBe(0);
    }

    [Fact]
    public async Task Delete_OwnOnly()
    {
        var user = await Data.UserAsync();
        var mine = await Data.NotificationAsync(user);
        var theirs = await Data.NotificationAsync(await Data.UserAsync());

        (await Notifications.DeleteAsync(user.ID, theirs.ID)).ShouldBeFalse();
        (await Notifications.DeleteAsync(user.ID, mine.ID)).ShouldBeTrue();
        (await Notifications.DeleteAsync(user.ID, mine.ID)).ShouldBeFalse();
        (await Query().Notifications.AnyAsync(n => n.ID == theirs.ID)).ShouldBeTrue();
    }

    [Fact]
    public async Task DeleteSeen_KeepsTheUnseenOnes()
    {
        var user = await Data.UserAsync();
        await Data.NotificationsAsync(user, 2, seen: true);
        await Data.NotificationsAsync(user, 1);

        (await Notifications.DeleteSeenAsync(user.ID)).ShouldBe(2);
        (await StoredAsync(user)).ShouldHaveSingleItem().IsSeen.ShouldBeFalse();
    }
}
