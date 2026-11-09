using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Services.Sync;

/* The changes feed of the offline sync (GET api/Sync/changes): the user's own reviews and collections whose
   rowversion is in [since, upper), plus the deletions (SyncTombstone) in that range, oldest change first.

   upper is MIN_ACTIVE_ROWVERSION(): every rowversion below it belongs to a committed write, so a transaction that
   is still running when the page is read can't be skipped (its rows get versions >= upper and come next time).
   Rowversions are unique in the database, so "last sent + 1" is an exact cursor between pages.

   Every collection change moves its rowversion (CollectionService sets UpdatedAt on item changes too), so a
   changed collection is sent whole, with its items. */
public class SyncChangesService : ISyncChangesService
{
    public const int DefaultLimit = 500;
    // Tombstones live for SyncRetention.Retention; a device that last synced longer ago than this (with a day of
    // margin) could have missed a deletion, so it gets everything again
    public static readonly TimeSpan FullResyncAfter = SyncRetention.Retention - TimeSpan.FromDays(1);

    private readonly AppDbContext _db;
    private readonly INotificationService _notifications;

    public SyncChangesService(AppDbContext db, INotificationService notifications)
    {
        _db = db;
        _notifications = notifications;
    }

    private enum ChangeKind { Review, Collection, DeletedReview, DeletedCollection }

    private sealed record Change(ChangeKind Kind, int Id, long Version);

    public async Task<SyncChangesDto> GetChangesAsync(int userId, long since, DateTime? syncedAt, bool continued, int limit,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, DefaultLimit);
        var now = DateTime.UtcNow;
        var fullResync = !continued && (since <= 0 || (syncedAt != null && syncedAt.Value.ToUniversalTime() < now - FullResyncAfter));
        var from = fullResync ? 0 : since;
        var upper = await MinActiveRowVersionAsync(cancellationToken);

        // limit + 1 of each kind: enough to fill the page and know whether more are waiting
        var changes = new List<Change>();
        changes.AddRange((await _db.Reviews.AsNoTracking()
                .Where(r => r.UserID == userId && r.RowVersion >= from && r.RowVersion < upper)
                .OrderBy(r => r.RowVersion).Take(limit + 1)
                .Select(r => new { r.ID, r.RowVersion })
                .ToListAsync(cancellationToken))
            .Select(r => new Change(ChangeKind.Review, r.ID, r.RowVersion)));
        changes.AddRange((await _db.Collections.AsNoTracking()
                .Where(c => c.UserID == userId && c.RowVersion >= from && c.RowVersion < upper)
                .OrderBy(c => c.RowVersion).Take(limit + 1)
                .Select(c => new { c.ID, c.RowVersion })
                .ToListAsync(cancellationToken))
            .Select(c => new Change(ChangeKind.Collection, c.ID, c.RowVersion)));
        changes.AddRange((await _db.SyncTombstones.AsNoTracking()
                .Where(t => t.UserID == userId && t.RowVersion >= from && t.RowVersion < upper &&
                            (t.EntityType == SyncEntityType.Review || t.EntityType == SyncEntityType.Collection))
                .OrderBy(t => t.RowVersion).Take(limit + 1)
                .Select(t => new { t.EntityType, t.EntityID, t.RowVersion })
                .ToListAsync(cancellationToken))
            .Select(t => new Change(t.EntityType == SyncEntityType.Review ? ChangeKind.DeletedReview : ChangeKind.DeletedCollection,
                t.EntityID, t.RowVersion)));

        var ordered = changes.OrderBy(c => c.Version).ToList();
        var page = ordered.Take(limit).ToList();
        var hasMore = ordered.Count > limit;
        var cursor = hasMore ? page[^1].Version + 1 : Math.Max(upper, from);

        var reviewIds = IdsOf(page, ChangeKind.Review);
        var collectionIds = IdsOf(page, ChangeKind.Collection);
        // Rows deleted after their version was read have a tombstone at a later version, sent next time
        var reviews = reviewIds.Count == 0 ? [] : await SyncStates.Reviews(_db)
            .Where(r => reviewIds.Contains(r.Id))
            .ToListAsync(cancellationToken);
        var collections = collectionIds.Count == 0 ? [] : await SyncStates.Collections(_db)
            .Where(c => collectionIds.Contains(c.Id))
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        return new SyncChangesDto
        {
            Reviews = reviews.OrderBy(r => r.Version).Select(SyncStates.AsUtc).ToList(),
            DeletedReviewIds = IdsOf(page, ChangeKind.DeletedReview),
            Collections = collections.OrderBy(c => c.Version).Select(SyncStates.AsUtc).ToList(),
            DeletedCollectionIds = IdsOf(page, ChangeKind.DeletedCollection),
            Cursor = cursor,
            HasMore = hasMore,
            FullResync = fullResync,
            Account = continued ? null : await AccountAsync(userId, cancellationToken),
            ServerTime = now
        };
    }

    private static List<int> IdsOf(List<Change> page, ChangeKind kind) =>
        page.Where(c => c.Kind == kind).Select(c => c.Id).Distinct().ToList();

    private async Task<long> MinActiveRowVersionAsync(CancellationToken cancellationToken) =>
        await _db.Database
            .SqlQueryRaw<long>("SELECT CONVERT(bigint, MIN_ACTIVE_ROWVERSION()) AS [Value]")
            .SingleAsync(cancellationToken);

    private async Task<SyncAccountDto> AccountAsync(int userId, CancellationToken cancellationToken)
    {
        var user = await _db.Users.AsNoTracking()
            .Where(u => u.ID == userId)
            .Select(u => new { u.ID, u.Username, u.Bio, u.ProfilePictureUrl })
            .SingleAsync(cancellationToken);

        var following = await _db.UserFollowers.AsNoTracking()
            .Where(f => f.FollowerID == userId)
            .OrderBy(f => f.Following.Username)
            .Select(f => new UserCompactDto { ID = f.Following.ID, Username = f.Following.Username, ProfilePictureUrl = f.Following.ProfilePictureUrl, IsFollowedByCurrentUser = true })
            .ToListAsync(cancellationToken);
        var followers = await _db.UserFollowers.AsNoTracking()
            .Where(f => f.FollowingID == userId)
            .OrderBy(f => f.Follower.Username)
            .Select(f => new UserCompactDto
            {
                ID = f.Follower.ID,
                Username = f.Follower.Username,
                ProfilePictureUrl = f.Follower.ProfilePictureUrl,
                IsFollowedByCurrentUser = _db.UserFollowers.Any(back => back.FollowerID == userId && back.FollowingID == f.FollowerID)
            })
            .ToListAsync(cancellationToken);

        return new SyncAccountDto
        {
            UserId = user.ID,
            Username = user.Username,
            Bio = user.Bio,
            ProfilePictureUrl = user.ProfilePictureUrl,
            Following = following,
            Followers = followers,
            Notifications = await _notifications.GetLatestAsync(userId),
            UnseenNotificationCount = await _notifications.GetUnseenCountAsync(userId)
        };
    }

    // The same rules as the single review / collection pages: own, public, or followers-only while following
    public async Task<SyncVisibleDto> GetVisibleAsync(int userId, IReadOnlyCollection<int> reviewIds, IReadOnlyCollection<int> collectionIds,
        CancellationToken cancellationToken = default)
    {
        var askedReviews = reviewIds.Distinct().ToList();
        var askedCollections = collectionIds.Distinct().ToList();

        var visibleReviews = askedReviews.Count == 0 ? [] : await _db.Reviews.AsNoTracking()
            .Where(r => askedReviews.Contains(r.ID) &&
                        (r.UserID == userId ||
                         r.VisibilityLevel == VisibilityLevel.Public ||
                         (r.VisibilityLevel == VisibilityLevel.FollowersOnly &&
                          _db.UserFollowers.Any(f => f.FollowerID == userId && f.FollowingID == r.UserID))))
            .Select(r => r.ID)
            .ToListAsync(cancellationToken);

        var visibleCollections = askedCollections.Count == 0 ? [] : await _db.Collections.AsNoTracking()
            .Where(c => askedCollections.Contains(c.ID) &&
                        (c.UserID == userId ||
                         c.VisibilityLevel == VisibilityLevel.Public ||
                         (c.VisibilityLevel == VisibilityLevel.FollowersOnly &&
                          _db.UserFollowers.Any(f => f.FollowerID == userId && f.FollowingID == c.UserID))))
            .Select(c => c.ID)
            .ToListAsync(cancellationToken);

        return new SyncVisibleDto(visibleReviews.Order().ToList(), visibleCollections.Order().ToList());
    }
}
