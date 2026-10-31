using Microsoft.AspNetCore.Http;
using NSubstitute;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services;
using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Tests.Unit;

public class UnlockHandlerTests
{
    private static readonly UnlockedAchievementDto Bronze = new() { GroupCode = "CRITIC", Title = "Critic", Tier = "Bronze" };
    private static readonly UnlockedAchievementDto Silver = new() { GroupCode = "CRITIC", Title = "Critic", Tier = "Silver" };

    [Fact]
    public async Task NotificationHandler_StoresOneNotificationPerTier()
    {
        var notifications = Substitute.For<INotificationService>();
        var handler = new NotificationUnlockHandler(notifications);

        await handler.HandleAsync(5, [Bronze, Silver]);

        await notifications.Received(2).CreateAsync(Arg.Is<Notification>(n =>
            n.RecipientUserID == 5 && n.Type == NotificationType.AchievementUnlocked && n.GroupCode == "CRITIC"));
        await notifications.Received(1).CreateAsync(Arg.Is<Notification>(n => n.Message == "You earned the Silver tier of Critic."));
    }

    private static (ResponseHeaderUnlockHandler Handler, DefaultHttpContext Context) HeaderHandler(int? requesterId)
    {
        var context = new DefaultHttpContext();
        var auth = Substitute.For<IUserAuthHelper>();
        auth.GetOptionalUserID().Returns(requesterId);
        return (new ResponseHeaderUnlockHandler(new HttpContextAccessor { HttpContext = context }, auth), context);
    }

    private static List<UnlockedAchievementDto>? Pending(HttpContext context) =>
        context.Items["UnlockedAchievements"] as List<UnlockedAchievementDto>;

    [Fact]
    public async Task HeaderHandler_CollectsEveryUnlockOfTheRequest()
    {
        var (handler, context) = HeaderHandler(requesterId: 5);

        await handler.HandleAsync(5, [Bronze]);
        await handler.HandleAsync(5, [Silver]);

        Pending(context).ShouldBe([Bronze, Silver]);
    }

    [Fact]
    public async Task HeaderHandler_IgnoresOtherUsersProgress()
    {
        var (handler, context) = HeaderHandler(requesterId: 6);

        await handler.HandleAsync(5, [Bronze]);

        Pending(context).ShouldBeNull();
    }

    [Fact]
    public async Task HeaderHandler_WithoutRequest_DoesNothing()
    {
        var handler = new ResponseHeaderUnlockHandler(new HttpContextAccessor(), Substitute.For<IUserAuthHelper>());

        await Should.NotThrowAsync(() => handler.HandleAsync(5, [Bronze]));
    }
}
