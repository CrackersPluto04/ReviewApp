using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services.Interfaces;
using ReviewApp.Api.Tests.Support;

namespace ReviewApp.Api.Tests.Integration;

public class ReviewReplyServiceTests : IntegrationTest
{
    private IReviewReplyService Replies => Service<IReviewReplyService>();

    private Task<(bool Success, bool NotFound, string Message, ReplyDto? Reply)> ReplyAsync(User user, Review review, string content = "Nice review", int? parent = null) =>
        Replies.CreateReplyAsync(user.ID, new CreateReplyDto { ReviewID = review.ID, ParentReplyID = parent, Content = content });

    private Task<List<Notification>> NotificationsOfAsync(User user) =>
        Query().Notifications.Where(n => n.RecipientUserID == user.ID).ToListAsync();

    /* Creating */

    [Fact]
    public async Task Create_ToAReview_TrimsAndReturnsTheOwnersView()
    {
        var author = await Data.UserAsync();
        var replier = await Data.UserAsync();
        var review = await Data.ReviewAsync(author);

        var (success, _, _, reply) = await ReplyAsync(replier, review, "  Great take!  ");

        success.ShouldBeTrue();
        reply!.Content.ShouldBe("Great take!");
        reply.Username.ShouldBe(replier.Username);
        reply.IsOwner.ShouldBeTrue();
        reply.ParentReplyID.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_EmptyContent_IsRejected(string content)
    {
        var review = await Data.ReviewAsync(await Data.UserAsync());

        var (success, notFound, message, _) = await ReplyAsync(await Data.UserAsync(), review, content);

        success.ShouldBeFalse();
        notFound.ShouldBeFalse();
        message.ShouldBe("Reply cannot be empty.");
    }

    [Fact]
    public async Task Create_ThirdLayerIsTheLimit()
    {
        var author = await Data.UserAsync();
        var review = await Data.ReviewAsync(author);
        var (_, _, _, layer2) = await ReplyAsync(author, review);
        var (layer3Success, _, _, layer3) = await ReplyAsync(author, review, parent: layer2!.ID);

        var (success, _, message, _) = await ReplyAsync(author, review, parent: layer3!.ID);

        layer3Success.ShouldBeTrue();
        success.ShouldBeFalse();
        message.ShouldBe("Replies can only be nested up to 3 levels.");
    }

    [Fact]
    public async Task Create_ParentFromAnotherReview_IsRejected()
    {
        var author = await Data.UserAsync();
        var review = await Data.ReviewAsync(author);
        var otherReview = await Data.ReviewAsync(author);
        var foreignParent = await Data.ReplyAsync(otherReview, author);

        var (success, notFound, _, _) = await ReplyAsync(author, review, parent: foreignParent.ID);

        success.ShouldBeFalse();
        notFound.ShouldBeFalse();
    }

    [Fact]
    public async Task Create_HiddenReview_IsNotFound()
    {
        var author = await Data.UserAsync();
        var review = await Data.ReviewAsync(author, visibility: VisibilityLevel.FollowersOnly);

        var (success, notFound, _, _) = await ReplyAsync(await Data.UserAsync(), review);

        success.ShouldBeFalse();
        notFound.ShouldBeTrue();
    }

    [Fact]
    public async Task Create_FollowerCanReplyToFollowersOnlyReview()
    {
        var author = await Data.UserAsync();
        var follower = await Data.UserAsync();
        await Data.FollowAsync(follower, author);
        var review = await Data.ReviewAsync(author, visibility: VisibilityLevel.FollowersOnly);

        (await ReplyAsync(follower, review)).Success.ShouldBeTrue();
    }

    [Fact]
    public async Task Create_PrivateReview_CantBeRepliedToEvenByItsAuthor()
    {
        var author = await Data.UserAsync();
        var review = await Data.ReviewAsync(author, visibility: VisibilityLevel.Private);

        var (success, notFound, message, _) = await ReplyAsync(author, review);

        success.ShouldBeFalse();
        notFound.ShouldBeFalse();
        message.ShouldBe("Private reviews can't be replied to.");
    }

    [Fact]
    public async Task Create_InsideADeletedRepliesThread_IsAllowedWithoutNotifyingAnyone()
    {
        var author = await Data.UserAsync();
        var deletedAuthor = await Data.UserAsync();
        var replier = await Data.UserAsync();
        var review = await Data.ReviewAsync(author);
        var deleted = await Data.ReplyAsync(review, deletedAuthor, deleted: true);

        (await ReplyAsync(replier, review, parent: deleted.ID)).Success.ShouldBeTrue();

        (await NotificationsOfAsync(deletedAuthor)).ShouldBeEmpty();
    }

    /* Notifications */

    [Fact]
    public async Task Create_NotifiesTheReviewAuthorForALayer2Reply()
    {
        var author = await Data.UserAsync();
        var replier = await Data.UserAsync();
        var review = await Data.ReviewAsync(author, await Data.MediaAsync(title: "Alien"));

        var (_, _, _, reply) = await ReplyAsync(replier, review, "Totally agree");

        var notification = (await NotificationsOfAsync(author)).Single();
        notification.Type.ShouldBe(NotificationType.ReviewReply);
        notification.ActorUserID.ShouldBe(replier.ID);
        notification.ReviewID.ShouldBe(review.ID);
        notification.ReplyID.ShouldBe(reply!.ID);
        notification.Message.ShouldBe($"{replier.Username} replied to your review of Alien: \"Totally agree\"");
    }

    [Fact]
    public async Task Create_NotifiesTheParentAuthorForALayer3Reply()
    {
        var author = await Data.UserAsync();
        var first = await Data.UserAsync();
        var second = await Data.UserAsync();
        var review = await Data.ReviewAsync(author);
        var parent = await Data.ReplyAsync(review, first);

        await ReplyAsync(second, review, parent: parent.ID);

        (await NotificationsOfAsync(first)).Single().Type.ShouldBe(NotificationType.ReplyReply);
        (await NotificationsOfAsync(author)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Create_NeverNotifiesAboutOwnReplies()
    {
        var author = await Data.UserAsync();
        var review = await Data.ReviewAsync(author);

        await ReplyAsync(author, review);

        (await NotificationsOfAsync(author)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Create_LongReply_IsShortenedInTheNotification()
    {
        var author = await Data.UserAsync();
        var review = await Data.ReviewAsync(author);

        await ReplyAsync(await Data.UserAsync(), review, new string('a', 300));

        var message = (await NotificationsOfAsync(author)).Single().Message;
        message.ShouldContain(new string('a', 80) + "…");
        message.ShouldNotContain(new string('a', 81));
    }

    [Fact]
    public async Task Create_RecipientWhoCantSeeTheReview_IsNotNotified()
    {
        var author = await Data.UserAsync();
        var follower = await Data.UserAsync();
        var replier = await Data.UserAsync();
        await Data.FollowAsync(follower, author);
        await Data.FollowAsync(replier, author);
        var review = await Data.ReviewAsync(author, visibility: VisibilityLevel.FollowersOnly);
        var parent = await Data.ReplyAsync(review, follower);
        // The follower stops following, so the review is hidden from them now
        await Query().UserFollowers.Where(f => f.FollowerID == follower.ID).ExecuteDeleteAsync();

        (await ReplyAsync(replier, review, parent: parent.ID)).Success.ShouldBeTrue();

        (await NotificationsOfAsync(follower)).ShouldBeEmpty();
    }

    /* Listing */

    [Fact]
    public async Task List_PagesOldestFirstWithKeysetCursor()
    {
        var author = await Data.UserAsync();
        var review = await Data.ReviewAsync(author);
        var replies = new List<ReviewReply>();
        for (var i = 0; i < 5; i++)
            replies.Add(await Data.ReplyAsync(review, author, content: $"reply {i}"));

        var first = await Replies.GetRepliesAsync(review.ID, null, null, 2, null);
        var second = await Replies.GetRepliesAsync(review.ID, null, first!.Items.Last().ID, 2, null);
        var last = await Replies.GetRepliesAsync(review.ID, null, second!.Items.Last().ID, 2, null);

        first.Items.Select(r => r.ID).ShouldBe([replies[0].ID, replies[1].ID]);
        first.HasMore.ShouldBeTrue();
        second.Items.Select(r => r.ID).ShouldBe([replies[2].ID, replies[3].ID]);
        last!.Items.Single().ID.ShouldBe(replies[4].ID);
        last.HasMore.ShouldBeFalse();
    }

    [Fact]
    public async Task List_SeparatesLayersAndCountsLiveChildren()
    {
        var author = await Data.UserAsync();
        var review = await Data.ReviewAsync(author);
        var parent = await Data.ReplyAsync(review, author);
        var child = await Data.ReplyAsync(review, author, parent: parent);
        await Data.ReplyAsync(review, author, parent: parent, deleted: true);

        var topLevel = await Replies.GetRepliesAsync(review.ID, null, null, 10, null);
        var children = await Replies.GetRepliesAsync(review.ID, parent.ID, null, 10, null);

        topLevel!.Items.Single().ChildCount.ShouldBe(1);
        children!.Items.Single().ID.ShouldBe(child.ID);
    }

    [Fact]
    public async Task List_DeletedReplyIsAPlaceholderOnlyWhileItHasLiveChildren()
    {
        var author = await Data.UserAsync();
        var review = await Data.ReviewAsync(author);
        var withChild = await Data.ReplyAsync(review, author, deleted: true, content: "secret");
        await Data.ReplyAsync(review, author, parent: withChild);
        await Data.ReplyAsync(review, author, deleted: true);

        var page = await Replies.GetRepliesAsync(review.ID, null, null, 10, author.ID);

        var placeholder = page!.Items.Single();
        placeholder.ID.ShouldBe(withChild.ID);
        placeholder.IsDeleted.ShouldBeTrue();
        placeholder.Content.ShouldBeNull();
        placeholder.Username.ShouldBeNull();
        placeholder.IsOwner.ShouldBeFalse();
    }

    [Fact]
    public async Task List_HiddenReviewOrForeignParent_IsNotFound()
    {
        var author = await Data.UserAsync();
        var hidden = await Data.ReviewAsync(author, visibility: VisibilityLevel.Private);
        var visible = await Data.ReviewAsync(author);
        var foreign = await Data.ReplyAsync(await Data.ReviewAsync(author), author);

        (await Replies.GetRepliesAsync(hidden.ID, null, null, 10, null)).ShouldBeNull();
        (await Replies.GetRepliesAsync(hidden.ID, null, null, 10, author.ID)).ShouldNotBeNull();
        (await Replies.GetRepliesAsync(visible.ID, foreign.ID, null, 10, null)).ShouldBeNull();
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(500, 50)]
    public async Task List_PageSizeIsClamped(int requested, int expected)
    {
        var author = await Data.UserAsync();
        var review = await Data.ReviewAsync(author);
        for (var i = 0; i < 51; i++)
            await Data.ReplyAsync(review, author);

        var page = await Replies.GetRepliesAsync(review.ID, null, null, requested, null);

        page!.Items.Count.ShouldBe(expected);
        page.HasMore.ShouldBeTrue();
    }

    /* Single reply and deleting */

    [Fact]
    public async Task Context_OfALayer3ReplyIncludesItsParent()
    {
        var author = await Data.UserAsync();
        var review = await Data.ReviewAsync(author);
        var parent = await Data.ReplyAsync(review, author, deleted: true);
        var child = await Data.ReplyAsync(review, author, parent: parent);

        var context = await Replies.GetReplyContextAsync(child.ID, null);

        context!.ReviewID.ShouldBe(review.ID);
        context.Reply.ID.ShouldBe(child.ID);
        context.ParentReply!.ID.ShouldBe(parent.ID);
        context.ParentReply.IsDeleted.ShouldBeTrue();
    }

    [Fact]
    public async Task Context_DeletedOrHiddenReply_IsNotFound()
    {
        var author = await Data.UserAsync();
        var review = await Data.ReviewAsync(author);
        var deleted = await Data.ReplyAsync(review, author, deleted: true);
        var hiddenReview = await Data.ReviewAsync(author, visibility: VisibilityLevel.FollowersOnly);
        var hidden = await Data.ReplyAsync(hiddenReview, author);

        (await Replies.GetReplyContextAsync(deleted.ID, author.ID)).ShouldBeNull();
        (await Replies.GetReplyContextAsync(hidden.ID, null)).ShouldBeNull();
        (await Replies.GetReplyContextAsync(hidden.ID, author.ID)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Delete_IsASoftDeleteByTheAuthorOnly()
    {
        var author = await Data.UserAsync();
        var other = await Data.UserAsync();
        var review = await Data.ReviewAsync(author);
        var reply = await Data.ReplyAsync(review, author, content: "kept in the database");

        (await Replies.DeleteReplyAsync(other.ID, reply.ID)).ShouldBeFalse();
        (await Replies.DeleteReplyAsync(author.ID, reply.ID)).ShouldBeTrue();
        (await Replies.DeleteReplyAsync(author.ID, reply.ID)).ShouldBeFalse();

        var stored = await Query().ReviewReplies.SingleAsync(r => r.ID == reply.ID);
        stored.IsDeleted.ShouldBeTrue();
        stored.Content.ShouldBe("kept in the database");
    }
}
