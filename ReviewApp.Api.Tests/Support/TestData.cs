using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services;

namespace ReviewApp.Api.Tests.Support;

// Builders for the rows a test needs. Every name and external ID is unique within the run,
// which is what keeps the tests isolated while they share one database.
public sealed class TestData : IDisposable
{
    public const string Password = "Passw0rd!";

    // A real BCrypt hash, so logins and password checks work; work factor 4 keeps the suite fast
    private static readonly string PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password, workFactor: 4);

    private static int _counter;

    private readonly AppDbContext _context = TestDatabase.CreateContext();

    // Unique per run; short enough for the 20 character username limit
    public static string Unique(string prefix = "u") => $"{prefix}{Interlocked.Increment(ref _counter)}";

    public async Task<User> UserAsync(Action<User>? configure = null)
    {
        var username = Unique();
        var user = new User
        {
            Username = username,
            Email = $"{username}@test.local",
            PasswordHash = PasswordHash,
            IsEmailConfirmed = true
        };
        configure?.Invoke(user);

        _context.Users.Add(user);
        await SaveAsync();
        return user;
    }

    // Created by the AddDefaultCollection trigger, never inserted by hand
    public Task<Collection> FavouritesAsync(User owner) =>
        _context.Collections.AsNoTracking().SingleAsync(c => c.UserID == owner.ID && c.Name == Collection.DefaultName);

    public async Task<Media> MediaAsync(MediaType type = MediaType.Movie, string? externalId = null, string? title = null)
    {
        Media media = type switch
        {
            MediaType.Movie => new Movie(),
            MediaType.Series => new Series(),
            _ => new Music()
        };
        media.MediaType = type;
        media.ExternalApiID = externalId ?? Unique("9");
        media.Title = title ?? $"{type} {media.ExternalApiID}";
        media.Creator = "Creator";

        _context.Media.Add(media);
        await SaveAsync();
        return media;
    }

    public async Task<Review> ReviewAsync(User author, Media? media = null, VisibilityLevel visibility = VisibilityLevel.Public,
        decimal score = 8.0m, string? text = "Worth watching.", DateTime? createdAt = null)
    {
        media ??= await MediaAsync();
        var review = new Review
        {
            UserID = author.ID,
            MediaID = media.ID,
            Score = score,
            ReviewText = text,
            VisibilityLevel = visibility,
            CreatedAt = createdAt ?? DateTime.UtcNow
        };

        _context.Reviews.Add(review);
        await SaveAsync();
        return review;
    }

    public async Task<ReviewReply> ReplyAsync(Review review, User author, ReviewReply? parent = null, bool deleted = false, string content = "A reply.")
    {
        var reply = new ReviewReply
        {
            ReviewID = review.ID,
            UserID = author.ID,
            ParentReplyID = parent?.ID,
            Content = content,
            IsDeleted = deleted
        };

        _context.ReviewReplies.Add(reply);
        await SaveAsync();
        return reply;
    }

    public async Task<Collection> CollectionAsync(User owner, VisibilityLevel visibility = VisibilityLevel.Public, string? name = null, params Media[] media)
    {
        var collection = new Collection { UserID = owner.ID, Name = name ?? Unique("Collection "), VisibilityLevel = visibility };
        _context.Collections.Add(collection);
        await SaveAsync();

        await AddToCollectionAsync(collection, media);
        return collection;
    }

    public async Task AddToCollectionAsync(Collection collection, params Media[] media)
    {
        var next = await _context.CollectionMedias.CountAsync(cm => cm.CollectionID == collection.ID);
        foreach (var item in media)
            _context.CollectionMedias.Add(new CollectionMedia { CollectionID = collection.ID, MediaID = item.ID, OrderIndex = next++ });
        await SaveAsync();
    }

    public async Task FollowAsync(User follower, User following)
    {
        _context.UserFollowers.Add(new UserFollower { FollowerID = follower.ID, FollowingID = following.ID });
        await SaveAsync();
    }

    public async Task<Notification> NotificationAsync(User recipient, NotificationType type = NotificationType.System,
        bool seen = false, DateTime? createdAt = null, User? actor = null)
    {
        var notification = new Notification
        {
            RecipientUserID = recipient.ID,
            ActorUserID = actor?.ID,
            Type = type,
            Title = "Title",
            Message = "Message",
            IsSeen = seen,
            CreatedAt = createdAt ?? DateTime.UtcNow
        };

        _context.Notifications.Add(notification);
        await SaveAsync();
        return notification;
    }

    // Stores a token the way SecurityService does (hash only) and returns the raw value a test can submit
    public async Task<(SecurityToken Token, string Raw)> SecurityTokenAsync(User user, SecurityTokenType type, string? raw = null,
        DateTime? expiresAt = null, DateTime? createdAt = null, bool used = false, int failedAttempts = 0)
    {
        raw ??= type is SecurityTokenType.EmailConfirmation or SecurityTokenType.EmailChange
            ? SecurityTokens.GenerateLinkToken()
            : SecurityTokens.GenerateCode();
        var now = DateTime.UtcNow;
        var token = new SecurityToken
        {
            UserID = user.ID,
            Type = type,
            TokenHash = SecurityTokens.Hash(raw),
            CreatedAt = createdAt ?? now,
            ExpiresAt = expiresAt ?? now.AddHours(1),
            IsUsed = used,
            FailedAttempts = failedAttempts
        };

        _context.SecurityTokens.Add(token);
        await SaveAsync();
        return (token, raw);
    }

    public async Task<UserSubscription> SubscriptionAsync(User user, SubscriptionStatus status, DateTime? periodEnd = null,
        bool cancelAtPeriodEnd = false, DateTime? pastDueSince = null, string? subscriptionId = null)
    {
        var subscription = new UserSubscription
        {
            UserID = user.ID,
            Provider = SubscriptionProvider.Fake,
            ProviderCustomerID = Unique("cus_"),
            ProviderSubscriptionID = subscriptionId ?? (status == SubscriptionStatus.None ? null : Unique("sub_")),
            Status = status,
            CancelAtPeriodEnd = cancelAtPeriodEnd,
            CurrentPeriodEnd = periodEnd,
            PastDueSince = pastDueSince
        };

        _context.UserSubscriptions.Add(subscription);
        await SaveAsync();
        return subscription;
    }

    public Task<UserSubscription> PremiumAsync(User user) =>
        SubscriptionAsync(user, SubscriptionStatus.Active, DateTime.UtcNow.AddDays(20));

    private async Task SaveAsync()
    {
        await _context.SaveChangesAsync();
        // Builders hand out detached rows, later builders don't drag earlier graphs along
        _context.ChangeTracker.Clear();
    }

    public void Dispose() => _context.Dispose();
}
