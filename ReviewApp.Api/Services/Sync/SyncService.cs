using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services.Interfaces;
using System.Text.Json;

namespace ReviewApp.Api.Services.Sync;

/* Applies the offline changes of the Android app (POST api/Sync/push), in the order they were made, each one on its
   own and answered on its own, so one problem never blocks or rolls back the others.

   Each op:
   1. Already applied (its OperationID is stored)? Answer with the stored result, change nothing.
   2. An op it depends on wasn't applied? Skipped.
   3. Parse and validate the payload; resolve clientRefs (reviews / collections created offline) to real IDs and
      media keys to stored media rows (TMDb / Spotify lookups happen here, outside any transaction).
   4. In one transaction: claim the OperationID (a parallel push of the same op waits here and then replays),
      lock the target row (UPDLOCK), compare with the base the device's change started from (FieldMerge), call the
      service's core write, and store the result with the claim. Conflicts and rejections roll everything back.
   5. After the commit: achievements and the new-follower notification, as if the user had just done it online.

   The web app keeps "last write wins" (no version checks there); only changes from the phone carry a base. */
public class SyncService : ISyncService
{
    // Per request
    public const int MaxOps = 50;
    // Media unknown to our database that may be looked up at TMDb / Spotify per request (the rest are deferred)
    public const int MaxNewMediaLookups = 20;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _db;
    private readonly IReviewService _reviews;
    private readonly ICollectionService _collections;
    private readonly IFollowerService _followers;
    private readonly INotificationService _notifications;
    private readonly IMediaService _media;
    private readonly IAchievementService _achievements;
    private readonly ILogger<SyncService> _logger;

    public SyncService(AppDbContext db, IReviewService reviews, ICollectionService collections, IFollowerService followers,
        INotificationService notifications, IMediaService media, IAchievementService achievements, ILogger<SyncService> logger)
    {
        _db = db;
        _reviews = reviews;
        _collections = collections;
        _followers = followers;
        _notifications = notifications;
        _media = media;
        _achievements = achievements;
        _logger = logger;
    }

    // What one request shares between its ops
    private sealed class Batch
    {
        public Dictionary<Guid, string> Statuses { get; } = [];
        public Dictionary<(MediaType, string), MediaResolution> Media { get; } = [];
        public int NewLookups { get; set; }
        public List<UnlockedAchievementDto> Unlocked { get; } = [];
    }

    private sealed record Op(int UserId, Guid OpId, string? ClientRef, Batch Batch, CancellationToken Ct);

    public async Task<SyncPushResponse> PushAsync(int userId, IReadOnlyList<SyncOpDto> ops, CancellationToken cancellationToken = default)
    {
        var batch = new Batch();
        var results = new List<SyncOpResult>(ops.Count);

        foreach (var dto in ops)
        {
            var op = new Op(userId, dto.OpId!.Value, dto.ClientRef, batch, cancellationToken);
            var result = await ProcessAsync(op, dto);
            batch.Statuses[op.OpId] = result.Status;
            results.Add(result);
        }

        return new SyncPushResponse(results, batch.Unlocked, DateTime.UtcNow);
    }

    private async Task<SyncOpResult> ProcessAsync(Op op, SyncOpDto dto)
    {
        try
        {
            _db.ChangeTracker.Clear();

            if (await StoredResultAsync(op) is { } replay)
                return replay;

            if (!await DependenciesAppliedAsync(op, dto.DependsOn))
                return Result(op, SyncStatus.Skipped, SyncReason.Dependency);

            if (!SyncOpParser.IsKnownType(dto.Type))
                return Result(op, SyncStatus.Rejected, SyncReason.UnknownType);

            var parsed = SyncOpParser.Parse(dto.Type!, dto.Payload, dto.ClientRef);
            if (parsed == null)
                return Result(op, SyncStatus.Rejected, SyncReason.Invalid);

            return parsed.Payload switch
            {
                ReviewCreatePayload p => await ReviewCreateAsync(op, p),
                ReviewUpdatePayload p => await ReviewUpdateAsync(op, p),
                ReviewDeletePayload p => await ReviewDeleteAsync(op, p),
                CollectionCreatePayload p => await CollectionCreateAsync(op, p),
                CollectionUpdatePayload p => await CollectionUpdateAsync(op, p),
                CollectionDeletePayload p => await CollectionDeleteAsync(op, p),
                CollectionItemPayload p when parsed.Type == SyncOpTypes.CollectionAddItem => await CollectionAddItemAsync(op, p),
                CollectionItemPayload p => await CollectionRemoveItemAsync(op, p),
                CollectionReorderPayload p => await CollectionReorderAsync(op, p),
                ProfileUpdatePayload p => await ProfileUpdateAsync(op, p),
                FollowSetPayload p => await FollowSetAsync(op, p),
                FollowerRemovePayload p => await FollowerRemoveAsync(op, p),
                NotificationSeenPayload p => await AtomicAsync(op, SyncEntityType.Notification, async () =>
                {
                    await _notifications.SetSeenAsync(op.UserId, p.Id!.Value, p.Seen!.Value);
                    return Result(op, SyncStatus.Applied, entityId: p.Id);
                }),
                NotificationDeletePayload p => await AtomicAsync(op, SyncEntityType.Notification, async () =>
                {
                    await _notifications.DeleteAsync(op.UserId, p.Id!.Value);
                    return Result(op, SyncStatus.Applied, entityId: p.Id);
                }),
                NotificationUpToPayload p => await AtomicAsync(op, SyncEntityType.Notification, async () =>
                {
                    if (parsed.Type == SyncOpTypes.NotificationMarkAllSeen)
                        await _notifications.MarkAllSeenAsync(op.UserId, p.UpToId);
                    else
                        await _notifications.DeleteSeenAsync(op.UserId, p.UpToId);
                    return Result(op, SyncStatus.Applied);
                }),
                _ => Result(op, SyncStatus.Rejected, SyncReason.UnknownType)
            };
        }
        catch (OperationCanceledException) when (op.Ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (DbErrors.IsTransient(ex))
        {
            _logger.LogWarning("Sync op {OpId} ({Type}) hit a deadlock or timeout, the device will retry it", op.OpId, dto.Type);
            return Result(op, SyncStatus.Retry, SyncReason.Busy);
        }
        catch (Exception ex)
        {
            // Committed changes are safe: the retry replays them. Anything else was rolled back.
            _logger.LogError(ex, "Sync op {OpId} ({Type}) failed", op.OpId, dto.Type);
            return Result(op, SyncStatus.Retry, SyncReason.ServerError);
        }
    }

    /* Reviews */

    private async Task<SyncOpResult> ReviewCreateAsync(Op op, ReviewCreatePayload p)
    {
        var (mediaId, failure) = await ResolveMediaAsync(op, p.Media!);
        if (failure != null) return failure;
        var createdAt = ClientTimestamp.Accept(p.CreatedAt, DateTime.UtcNow);

        return await AtomicAsync(op, SyncEntityType.Review, async () =>
        {
            var write = await _reviews.CreateCoreAsync(op.UserId, mediaId, p.Fields!.ToFields(), createdAt);
            var state = await SyncStates.ReviewAsync(_db, write.Review!.ID, op.Ct);
            return write.Status == ReviewWriteStatus.Created
                ? Result(op, SyncStatus.Applied, entityId: write.Review.ID, entity: state)
                // The user reviewed it elsewhere meanwhile: keep mine (an update) or theirs, the user decides
                : Result(op, SyncStatus.Conflict, SyncReason.AlreadyReviewed, write.Review.ID, state);
        }, afterCommit: () => EvaluateAsync(op, ReviewService.CreateMetrics(p.Media!.Type!.Value)));
    }

    private async Task<SyncOpResult> ReviewUpdateAsync(Op op, ReviewUpdatePayload p)
    {
        var reviewId = await ResolveRefAsync(op, p.Review!, SyncEntityType.Review);
        if (reviewId == null) return Result(op, SyncStatus.Skipped, SyncReason.Dependency);

        return await AtomicAsync(op, SyncEntityType.Review, async () =>
        {
            var review = await LockReviewAsync(op, reviewId.Value);
            // Deleted elsewhere (or never the user's): recreate or discard, the user decides
            if (review == null) return Result(op, SyncStatus.Conflict, SyncReason.Deleted, reviewId);

            var server = new ReviewFields(review.Score, review.ReviewText, review.Pros, review.Cons, review.VisibilityLevel);
            var mine = p.Fields!.ToFields();
            var merge = p.Base == null || p.BaseVersion == review.RowVersion
                ? new MergeOutcome<ReviewFields>(mine, [], false)
                : FieldMerge.Review(p.Base.ToFields(), mine, server);

            if (merge.HasConflicts)
                return Result(op, SyncStatus.Conflict, SyncReason.FieldConflict, review.ID, await SyncStates.ReviewAsync(_db, review.ID, op.Ct))
                    with { ConflictFields = [.. merge.Conflicts] };

            if (merge.Value != server)
                await _reviews.UpdateCoreAsync(op.UserId, review.ID, merge.Value);

            var status = merge.KeptServerChanges ? SyncStatus.Merged : SyncStatus.Applied;
            return Result(op, status, entityId: review.ID, entity: await SyncStates.ReviewAsync(_db, review.ID, op.Ct));
        }, afterCommit: () => EvaluateAsync(op, ReviewService.UpdateMetrics));
    }

    private async Task<SyncOpResult> ReviewDeleteAsync(Op op, ReviewDeletePayload p)
    {
        var reviewId = await ResolveRefAsync(op, p.Review!, SyncEntityType.Review);
        if (reviewId == null) return Result(op, SyncStatus.Skipped, SyncReason.Dependency);

        return await AtomicAsync(op, SyncEntityType.Review, async () =>
        {
            var review = await LockReviewAsync(op, reviewId.Value);
            // Already gone
            if (review == null) return Result(op, SyncStatus.Applied, entityId: reviewId);

            // Edited elsewhere after the user decided to delete it: delete anyway or keep it, the user decides
            if (p.BaseVersion != null && p.BaseVersion != review.RowVersion)
                return Result(op, SyncStatus.Conflict, SyncReason.ModifiedSince, review.ID, await SyncStates.ReviewAsync(_db, review.ID, op.Ct));

            await _reviews.DeleteCoreAsync(op.UserId, review.ID);
            return Result(op, SyncStatus.Applied, entityId: review.ID);
        });
    }

    /* Collections */

    private async Task<SyncOpResult> CollectionCreateAsync(Op op, CollectionCreatePayload p)
    {
        var fields = p.Fields!.ToFields();

        return await AtomicAsync(op, SyncEntityType.Collection, async () =>
        {
            var write = await _collections.CreateCoreAsync(op.UserId, fields.Name, fields.Visibility);
            return write.Status switch
            {
                CollectionWriteStatus.Created =>
                    Result(op, SyncStatus.Applied, entityId: write.Collection!.ID, entity: await SyncStates.CollectionAsync(_db, write.Collection.ID, op.Ct)),
                // Another name or merge into the existing one, the user decides
                CollectionWriteStatus.NameTaken => await NameTakenAsync(op, write.ExistingCollectionID),
                _ => Result(op, SyncStatus.Rejected, SyncReason.Invalid)
            };
        }, afterCommit: () => EvaluateAsync(op, CollectionService.CreateMetrics));
    }

    private async Task<SyncOpResult> CollectionUpdateAsync(Op op, CollectionUpdatePayload p)
    {
        var collectionId = await ResolveRefAsync(op, p.Collection!, SyncEntityType.Collection);
        if (collectionId == null) return Result(op, SyncStatus.Skipped, SyncReason.Dependency);

        return await AtomicAsync(op, SyncEntityType.Collection, async () =>
        {
            var collection = await LockCollectionAsync(op, collectionId.Value);
            if (collection == null) return Result(op, SyncStatus.Conflict, SyncReason.Deleted, collectionId);
            if (collection.Name == Collection.DefaultName) return Result(op, SyncStatus.Rejected, SyncReason.Locked, collection.ID);

            var server = new CollectionFields(collection.Name, collection.VisibilityLevel);
            var mine = p.Fields!.ToFields();
            // The version also moves with item changes, so a different version alone isn't a conflict: the fields decide
            var merge = p.Base == null || p.BaseVersion == collection.RowVersion
                ? new MergeOutcome<CollectionFields>(mine, [], false)
                : FieldMerge.Collection(p.Base.ToFields(), mine, server);

            if (merge.HasConflicts)
                return Result(op, SyncStatus.Conflict, SyncReason.FieldConflict, collection.ID, await SyncStates.CollectionAsync(_db, collection.ID, op.Ct))
                    with { ConflictFields = [.. merge.Conflicts] };

            var status = merge.KeptServerChanges ? SyncStatus.Merged : SyncStatus.Applied;
            if (merge.Value == server)
                return Result(op, status, entityId: collection.ID, entity: await SyncStates.CollectionAsync(_db, collection.ID, op.Ct));

            var write = await _collections.UpdateCoreAsync(op.UserId, collection.ID, merge.Value.Name, merge.Value.Visibility);
            return write.Status switch
            {
                CollectionWriteStatus.Updated =>
                    Result(op, status, entityId: collection.ID, entity: await SyncStates.CollectionAsync(_db, collection.ID, op.Ct)),
                CollectionWriteStatus.NameTaken => await NameTakenAsync(op, write.ExistingCollectionID),
                CollectionWriteStatus.Locked => Result(op, SyncStatus.Rejected, SyncReason.Locked, collection.ID),
                _ => Result(op, SyncStatus.Rejected, SyncReason.Invalid)
            };
        });
    }

    private async Task<SyncOpResult> CollectionDeleteAsync(Op op, CollectionDeletePayload p)
    {
        var collectionId = await ResolveRefAsync(op, p.Collection!, SyncEntityType.Collection);
        if (collectionId == null) return Result(op, SyncStatus.Skipped, SyncReason.Dependency);

        return await AtomicAsync(op, SyncEntityType.Collection, async () =>
        {
            var collection = await LockCollectionAsync(op, collectionId.Value);
            if (collection == null) return Result(op, SyncStatus.Applied, entityId: collectionId);
            if (collection.Name == Collection.DefaultName) return Result(op, SyncStatus.Rejected, SyncReason.Locked, collection.ID);

            await _collections.DeleteCoreAsync(op.UserId, collection.ID);
            return Result(op, SyncStatus.Applied, entityId: collection.ID);
        });
    }

    private async Task<SyncOpResult> CollectionAddItemAsync(Op op, CollectionItemPayload p)
    {
        var collectionId = await ResolveRefAsync(op, p.Collection!, SyncEntityType.Collection);
        if (collectionId == null) return Result(op, SyncStatus.Skipped, SyncReason.Dependency);

        // Before any media lookup: adding to a deleted collection is the user's decision (recreate it or discard)
        if (!await _db.Collections.AnyAsync(c => c.ID == collectionId && c.UserID == op.UserId, op.Ct))
            return Result(op, SyncStatus.Conflict, SyncReason.CollectionDeleted, collectionId);

        var (mediaId, failure) = await ResolveMediaAsync(op, p.Media!);
        if (failure != null) return failure;

        return await AtomicAsync(op, SyncEntityType.CollectionItem, async () =>
        {
            var collection = await LockCollectionAsync(op, collectionId.Value);
            if (collection == null) return Result(op, SyncStatus.Conflict, SyncReason.CollectionDeleted, collectionId);

            // Added and AlreadyPresent are both done
            await _collections.AddMediaCoreAsync(op.UserId, collection.ID, mediaId);
            return Result(op, SyncStatus.Applied, entityId: collection.ID, entity: await SyncStates.CollectionAsync(_db, collection.ID, op.Ct));
        });
    }

    private async Task<SyncOpResult> CollectionRemoveItemAsync(Op op, CollectionItemPayload p)
    {
        var collectionId = await ResolveRefAsync(op, p.Collection!, SyncEntityType.Collection);
        if (collectionId == null) return Result(op, SyncStatus.Skipped, SyncReason.Dependency);

        // A media row we never stored can't be in any collection: nothing to do
        var mediaId = await StoredMediaIdAsync(p.Media!, op.Ct);

        return await AtomicAsync(op, SyncEntityType.CollectionItem, async () =>
        {
            var collection = await LockCollectionAsync(op, collectionId.Value);
            // A deleted collection has no items to remove
            if (collection == null) return Result(op, SyncStatus.Applied, entityId: collectionId);

            if (mediaId != null)
                await _collections.RemoveMediaCoreAsync(op.UserId, collection.ID, mediaId.Value);
            return Result(op, SyncStatus.Applied, entityId: collection.ID, entity: await SyncStates.CollectionAsync(_db, collection.ID, op.Ct));
        });
    }

    private async Task<SyncOpResult> CollectionReorderAsync(Op op, CollectionReorderPayload p)
    {
        var collectionId = await ResolveRefAsync(op, p.Collection!, SyncEntityType.Collection);
        if (collectionId == null) return Result(op, SyncStatus.Skipped, SyncReason.Dependency);

        var order = await StoredMediaIdsAsync(p.Items!, op.Ct);

        return await AtomicAsync(op, SyncEntityType.Collection, async () =>
        {
            var collection = await LockCollectionAsync(op, collectionId.Value);
            if (collection == null) return Result(op, SyncStatus.Conflict, SyncReason.CollectionDeleted, collectionId);

            // The device's order for the items still there, the rest after them (CollectionService.MergeOrder)
            await _collections.SetOrderAsync(op.UserId, collection.ID, order);
            return Result(op, SyncStatus.Applied, entityId: collection.ID, entity: await SyncStates.CollectionAsync(_db, collection.ID, op.Ct));
        });
    }

    private async Task<SyncOpResult> NameTakenAsync(Op op, int? existingId) =>
        Result(op, SyncStatus.Conflict, SyncReason.NameTaken, existingId,
            existingId == null ? null : await SyncStates.CollectionAsync(_db, existingId.Value, op.Ct));

    /* Profile, follows: the latest intent wins */

    private async Task<SyncOpResult> ProfileUpdateAsync(Op op, ProfileUpdatePayload p)
    {
        return await AtomicAsync(op, SyncEntityType.Profile, async () =>
        {
            var user = await _db.Users.FirstAsync(u => u.ID == op.UserId, op.Ct);
            var previous = new Dictionary<string, string?>();

            if (p.Fields!.Bio != null)
                user.Bio = Overwrite("bio", user.Bio, p.Base?.Bio, p.Fields.Bio, previous);
            if (p.Fields.ProfilePictureUrl != null)
                user.ProfilePictureUrl = Overwrite("profilePictureUrl", user.ProfilePictureUrl, p.Base?.ProfilePictureUrl, p.Fields.ProfilePictureUrl, previous);

            await _db.SaveChangesAsync(op.Ct);
            return Result(op, SyncStatus.Applied, entityId: user.ID,
                entity: new SyncProfileDto { Bio = user.Bio, ProfilePictureUrl = user.ProfilePictureUrl }) with
            {
                Previous = previous.Count > 0 ? previous : null
            };
        });

        // The new value (empty = cleared); notes the overwritten one if it wasn't what the device had seen
        string? Overwrite(string field, string? current, string? seen, string value, Dictionary<string, string?> previous)
        {
            var next = value.Length == 0 ? null : value;
            if (p.Base != null && !SameText(current, seen) && !SameText(current, next))
                previous[field] = current;
            return next;
        }
    }

    private async Task<SyncOpResult> FollowSetAsync(Op op, FollowSetPayload p)
    {
        var status = FollowWriteStatus.Unchanged;

        return await AtomicAsync(op, SyncEntityType.Follow, async () =>
        {
            status = await _followers.SetFollowAsync(op.UserId, p.UserId!.Value, p.Follow!.Value);
            return status switch
            {
                FollowWriteStatus.Self => Result(op, SyncStatus.Rejected, SyncReason.Self, p.UserId),
                FollowWriteStatus.TargetMissing => Result(op, SyncStatus.Rejected, SyncReason.UserNotFound, p.UserId),
                // Changed, or already in the requested state
                _ => Result(op, SyncStatus.Applied, entityId: p.UserId)
            };
        }, afterCommit: async () =>
        {
            if (p.Follow == true && status == FollowWriteStatus.Changed)
                await _notifications.NotifyNewFollowerAsync(op.UserId, p.UserId!.Value);
        });
    }

    private Task<SyncOpResult> FollowerRemoveAsync(Op op, FollowerRemovePayload p) =>
        AtomicAsync(op, SyncEntityType.Follow, async () =>
        {
            await _followers.RemoveFollowerCoreAsync(op.UserId, p.UserId!.Value);
            return Result(op, SyncStatus.Applied, entityId: p.UserId);
        });

    /* Steps shared by the ops */

    // Runs one op atomically: claims its OperationID first (a parallel push of the same op blocks on that unique row
    // until this one ends, then replays its result), applies the change, and keeps the claim, with the result,
    // only if the change was applied. Conflicts and rejections roll back, so the device can send them again later.
    private async Task<SyncOpResult> AtomicAsync(Op op, SyncEntityType entityType, Func<Task<SyncOpResult>> apply, Func<Task>? afterCommit = null)
    {
        _db.ChangeTracker.Clear();
        await using var transaction = await _db.Database.BeginTransactionAsync(op.Ct);

        var record = new SyncOperation
        {
            UserID = op.UserId,
            OperationID = op.OpId,
            ClientRef = op.ClientRef,
            EntityType = entityType
        };
        _db.SyncOperations.Add(record);
        try
        {
            await _db.SaveChangesAsync(op.Ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            await transaction.RollbackAsync(op.Ct);
            _db.ChangeTracker.Clear();
            // The same op was applied by a parallel request; otherwise another op already used this clientRef
            return await StoredResultAsync(op) ?? Result(op, SyncStatus.Rejected, SyncReason.ClientRefTaken);
        }

        var result = await apply();
        if (result.Status is not (SyncStatus.Applied or SyncStatus.Merged))
        {
            await transaction.RollbackAsync(op.Ct);
            return result;
        }

        record.EntityID = result.EntityId;
        record.ResultJson = JsonSerializer.Serialize(result, JsonOptions);
        await _db.SaveChangesAsync(op.Ct);
        await transaction.CommitAsync(op.Ct);

        if (afterCommit != null)
            await afterCommit();
        return result;
    }

    private async Task<SyncOpResult?> StoredResultAsync(Op op)
    {
        var json = await _db.SyncOperations.AsNoTracking()
            .Where(s => s.UserID == op.UserId && s.OperationID == op.OpId)
            .Select(s => s.ResultJson)
            .FirstOrDefaultAsync(op.Ct);
        return string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<SyncOpResult>(json, JsonOptions);
    }

    // Each dependency was applied: earlier in this request, or stored from an earlier one
    private async Task<bool> DependenciesAppliedAsync(Op op, List<Guid>? dependsOn)
    {
        if (dependsOn is not { Count: > 0 })
            return true;

        var earlier = new List<Guid>();
        foreach (var dependency in dependsOn.Distinct())
        {
            if (op.Batch.Statuses.TryGetValue(dependency, out var status))
            {
                if (status is not (SyncStatus.Applied or SyncStatus.Merged))
                    return false;
            }
            else
                earlier.Add(dependency);
        }

        if (earlier.Count == 0)
            return true;
        var stored = await _db.SyncOperations.CountAsync(s => s.UserID == op.UserId && earlier.Contains(s.OperationID), op.Ct);
        return stored == earlier.Count;
    }

    // The real ID of a review / collection: given, or created by an applied op with this clientRef (in this request
    // or an earlier one). Null = that op hasn't been applied (yet).
    private async Task<int?> ResolveRefAsync(Op op, EntityRefDto entity, SyncEntityType entityType)
    {
        if (entity.Id is { } id)
            return id;

        return await _db.SyncOperations.AsNoTracking()
            .Where(s => s.UserID == op.UserId && s.ClientRef == entity.Ref && s.EntityType == entityType)
            .Select(s => s.EntityID)
            .FirstOrDefaultAsync(op.Ct);
    }

    // The stored media row of a key, fetched from TMDb / Spotify if it's new. Lookups are capped per request and
    // remembered within it; an outage leaves the op to retry, an unknown ID rejects it.
    private async Task<(int MediaId, SyncOpResult? Failure)> ResolveMediaAsync(Op op, MediaKeyDto key)
    {
        var type = key.Type!.Value;
        var externalId = key.ExternalApiId!;

        if (!op.Batch.Media.TryGetValue((type, externalId), out var resolution))
        {
            var stored = await StoredMediaIdAsync(key, op.Ct);
            if (stored != null)
                resolution = new MediaResolution(MediaLookupStatus.Found, stored.Value);
            else
            {
                if (op.Batch.NewLookups >= MaxNewMediaLookups)
                    return (0, Result(op, SyncStatus.Retry, SyncReason.Deferred));
                op.Batch.NewLookups++;
                resolution = await _media.ResolveAsync(type, externalId, op.Ct);
            }
            op.Batch.Media[(type, externalId)] = resolution;
        }

        return resolution.Status switch
        {
            MediaLookupStatus.Found => (resolution.MediaID, null),
            MediaLookupStatus.Unavailable => (0, Result(op, SyncStatus.Retry, SyncReason.SourceUnavailable)),
            _ => (0, Result(op, SyncStatus.Rejected, SyncReason.MediaNotFound))
        };
    }

    private Task<int?> StoredMediaIdAsync(MediaKeyDto key, CancellationToken cancellationToken) =>
        _db.Media
            .Where(m => m.MediaType == key.Type && m.ExternalApiID == key.ExternalApiId)
            .Select(m => (int?)m.ID)
            .FirstOrDefaultAsync(cancellationToken);

    // The stored media IDs of the keys, in their order; keys we never stored are left out (they can't be in a collection)
    private async Task<List<int>> StoredMediaIdsAsync(List<MediaKeyDto> keys, CancellationToken cancellationToken)
    {
        var ids = new Dictionary<(MediaType, string), int>();
        foreach (var group in keys.GroupBy(k => k.Type!.Value))
        {
            var externalIds = group.Select(k => k.ExternalApiId!).Distinct().ToList();
            var rows = await _db.Media.AsNoTracking()
                .Where(m => m.MediaType == group.Key && externalIds.Contains(m.ExternalApiID))
                .Select(m => new { m.ID, m.ExternalApiID })
                .ToListAsync(cancellationToken);
            foreach (var row in rows)
                ids[(group.Key, row.ExternalApiID)] = row.ID;
        }

        return keys
            .Select(k => ids.TryGetValue((k.Type!.Value, k.ExternalApiId!), out var id) ? id : (int?)null)
            .OfType<int>()
            .ToList();
    }

    // The user's row, locked until the op's transaction ends, so a parallel change of it waits instead of interleaving
    private Task<Review?> LockReviewAsync(Op op, int reviewId) =>
        _db.Reviews
            .FromSqlInterpolated($"SELECT * FROM Reviews WITH (UPDLOCK, ROWLOCK) WHERE ID = {reviewId} AND UserID = {op.UserId}")
            .FirstOrDefaultAsync(op.Ct);

    private Task<Collection?> LockCollectionAsync(Op op, int collectionId) =>
        _db.Collections
            .FromSqlInterpolated($"SELECT * FROM Collections WITH (UPDLOCK, ROWLOCK) WHERE ID = {collectionId} AND UserID = {op.UserId}")
            .FirstOrDefaultAsync(op.Ct);

    private async Task EvaluateAsync(Op op, params AchievementMetric[] metrics) =>
        op.Batch.Unlocked.AddRange(await _achievements.EvaluateAsync(op.UserId, metrics));

    private static bool SameText(string? a, string? b) =>
        string.Equals(string.IsNullOrEmpty(a) ? null : a, string.IsNullOrEmpty(b) ? null : b, StringComparison.Ordinal);

    private static SyncOpResult Result(Op op, string status, string? reason = null, int? entityId = null, object? entity = null) => new()
    {
        OpId = op.OpId,
        Status = status,
        Reason = reason,
        EntityId = entityId,
        Entity = entity == null ? null : JsonSerializer.SerializeToElement(entity, entity.GetType(), JsonOptions)
    };
}
