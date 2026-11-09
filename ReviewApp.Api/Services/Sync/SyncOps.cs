using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace ReviewApp.Api.Services.Sync;

// The kinds of offline changes the sync accepts
public static class SyncOpTypes
{
    public const string ReviewCreate = "review.create";
    public const string ReviewUpdate = "review.update";
    public const string ReviewDelete = "review.delete";
    public const string CollectionCreate = "collection.create";
    public const string CollectionUpdate = "collection.update";
    public const string CollectionDelete = "collection.delete";
    public const string CollectionAddItem = "collection.addItem";
    public const string CollectionRemoveItem = "collection.removeItem";
    public const string CollectionReorder = "collection.reorder";
    public const string ProfileUpdate = "profile.update";
    public const string FollowSet = "follow.set";
    public const string FollowerRemove = "follower.remove";
    public const string NotificationSeen = "notification.seen";
    public const string NotificationDelete = "notification.delete";
    public const string NotificationMarkAllSeen = "notification.markAllSeen";
    public const string NotificationDeleteSeen = "notification.deleteSeen";

    // The ops that create something, so they may carry a clientRef
    public static bool Creates(string type) => type is ReviewCreate or CollectionCreate;
}

/* Payloads. Nullable members with [Required] make a missing field a validation error instead of a silent default. */

// A review or collection: its real ID, or the clientRef of the op that created it on the device
public record EntityRefDto
{
    public int? Id { get; init; }
    [MaxLength(DAL.Entities.SyncOperation.MaxClientRefLength)]
    public string? Ref { get; init; }
}

public record MediaKeyDto
{
    [Required]
    public MediaType? Type { get; init; }
    [Required, MaxLength(MediaService.MaxExternalIdLength)]
    public string? ExternalApiId { get; init; }
}

public record ReviewFieldsDto
{
    [Required, Range(1.0, 10.0)]
    public decimal? Score { get; init; }
    [MaxLength(500)]
    public string? ReviewText { get; init; }
    [MaxLength(250)]
    public string? Pros { get; init; }
    [MaxLength(250)]
    public string? Cons { get; init; }
    [Required]
    public VisibilityLevel? Visibility { get; init; }

    public ReviewFields ToFields() => new(Score!.Value, ReviewText, Pros, Cons, Visibility!.Value);
}

public record CollectionFieldsDto
{
    [Required, MaxLength(CollectionService.MaxNameLength)]
    public string? Name { get; init; }
    [Required]
    public VisibilityLevel? Visibility { get; init; }

    public CollectionFields ToFields() => new(Name!, Visibility!.Value);
}

// A field left out (null) is not changed. An empty string clears it.
public record ProfileFieldsDto
{
    [MaxLength(150)]
    public string? Bio { get; init; }
    [MaxLength(2048)]
    public string? ProfilePictureUrl { get; init; }
}

public record ReviewCreatePayload
{
    [Required]
    public MediaKeyDto? Media { get; init; }
    [Required]
    public ReviewFieldsDto? Fields { get; init; }
    // When the user wrote it; used only within ClientTimestamp's bounds
    public DateTimeOffset? CreatedAt { get; init; }
}

// Base: the values the device's edit started from (null = the device's values win everywhere).
// BaseVersion: the server version they came from; equal to the current one = nobody else changed it.
public record ReviewUpdatePayload
{
    [Required]
    public EntityRefDto? Review { get; init; }
    public long? BaseVersion { get; init; }
    public ReviewFieldsDto? Base { get; init; }
    [Required]
    public ReviewFieldsDto? Fields { get; init; }
}

// BaseVersion: the version the user saw when deleting; changed since = conflict (null = delete whatever is there)
public record ReviewDeletePayload
{
    [Required]
    public EntityRefDto? Review { get; init; }
    public long? BaseVersion { get; init; }
}

public record CollectionCreatePayload
{
    [Required]
    public CollectionFieldsDto? Fields { get; init; }
}

public record CollectionUpdatePayload
{
    [Required]
    public EntityRefDto? Collection { get; init; }
    public long? BaseVersion { get; init; }
    public CollectionFieldsDto? Base { get; init; }
    [Required]
    public CollectionFieldsDto? Fields { get; init; }
}

public record CollectionDeletePayload
{
    [Required]
    public EntityRefDto? Collection { get; init; }
}

public record CollectionItemPayload
{
    [Required]
    public EntityRefDto? Collection { get; init; }
    [Required]
    public MediaKeyDto? Media { get; init; }
}

// The device's order of the items (all it knew of); items it didn't know keep their place after them
public record CollectionReorderPayload
{
    [Required]
    public EntityRefDto? Collection { get; init; }
    [Required, MaxLength(1000)]
    public List<MediaKeyDto>? Items { get; init; }
}

// Base: the values the device had seen, so the result can mention what was overwritten
public record ProfileUpdatePayload
{
    [Required]
    public ProfileFieldsDto? Fields { get; init; }
    public ProfileFieldsDto? Base { get; init; }
}

public record FollowSetPayload
{
    [Required, Range(1, int.MaxValue)]
    public int? UserId { get; init; }
    [Required]
    public bool? Follow { get; init; }
}

public record FollowerRemovePayload
{
    [Required, Range(1, int.MaxValue)]
    public int? UserId { get; init; }
}

public record NotificationSeenPayload
{
    [Required, Range(1, int.MaxValue)]
    public int? Id { get; init; }
    [Required]
    public bool? Seen { get; init; }
}

public record NotificationDeletePayload
{
    [Required, Range(1, int.MaxValue)]
    public int? Id { get; init; }
}

// Mark all seen / delete all seen, but only up to the newest notification the device had
public record NotificationUpToPayload
{
    [Required, Range(1, int.MaxValue)]
    public int? UpToId { get; init; }
}

// The editable fields of a collection
public record CollectionFields(string Name, VisibilityLevel Visibility);

public record ParsedSyncOp(string Type, object Payload);

// Turns an op's JSON payload into its typed payload, checking every field (the controller's model validation
// doesn't reach into the payload). Null = invalid; the op is rejected without touching anything.
public static class SyncOpParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly Dictionary<string, Type> PayloadTypes = new()
    {
        [SyncOpTypes.ReviewCreate] = typeof(ReviewCreatePayload),
        [SyncOpTypes.ReviewUpdate] = typeof(ReviewUpdatePayload),
        [SyncOpTypes.ReviewDelete] = typeof(ReviewDeletePayload),
        [SyncOpTypes.CollectionCreate] = typeof(CollectionCreatePayload),
        [SyncOpTypes.CollectionUpdate] = typeof(CollectionUpdatePayload),
        [SyncOpTypes.CollectionDelete] = typeof(CollectionDeletePayload),
        [SyncOpTypes.CollectionAddItem] = typeof(CollectionItemPayload),
        [SyncOpTypes.CollectionRemoveItem] = typeof(CollectionItemPayload),
        [SyncOpTypes.CollectionReorder] = typeof(CollectionReorderPayload),
        [SyncOpTypes.ProfileUpdate] = typeof(ProfileUpdatePayload),
        [SyncOpTypes.FollowSet] = typeof(FollowSetPayload),
        [SyncOpTypes.FollowerRemove] = typeof(FollowerRemovePayload),
        [SyncOpTypes.NotificationSeen] = typeof(NotificationSeenPayload),
        [SyncOpTypes.NotificationDelete] = typeof(NotificationDeletePayload),
        [SyncOpTypes.NotificationMarkAllSeen] = typeof(NotificationUpToPayload),
        [SyncOpTypes.NotificationDeleteSeen] = typeof(NotificationUpToPayload)
    };

    public static bool IsKnownType(string? type) => type != null && PayloadTypes.ContainsKey(type);

    public static ParsedSyncOp? Parse(string type, JsonElement payload, string? clientRef = null)
    {
        if (!PayloadTypes.TryGetValue(type, out var payloadType) || payload.ValueKind != JsonValueKind.Object)
            return null;
        if (clientRef != null && (!SyncOpTypes.Creates(type) || string.IsNullOrWhiteSpace(clientRef)))
            return null;

        object? parsed;
        try
        {
            parsed = payload.Deserialize(payloadType, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        return parsed != null && IsValid(parsed) ? new ParsedSyncOp(type, parsed) : null;
    }

    private static bool IsValid(object payload) => payload switch
    {
        ReviewCreatePayload p => Valid(p, p.Media, p.Fields) && ValidMedia(p.Media!) && ValidReview(p.Fields!),
        ReviewUpdatePayload p => Valid(p, p.Review, p.Base, p.Fields) && ValidRef(p.Review!) &&
                                 ValidReview(p.Fields!) && (p.Base == null || ValidReview(p.Base)),
        ReviewDeletePayload p => Valid(p, p.Review) && ValidRef(p.Review!),
        CollectionCreatePayload p => Valid(p, p.Fields) && ValidCollection(p.Fields!),
        CollectionUpdatePayload p => Valid(p, p.Collection, p.Base, p.Fields) && ValidRef(p.Collection!) &&
                                     ValidCollection(p.Fields!) && (p.Base == null || ValidCollection(p.Base)),
        CollectionDeletePayload p => Valid(p, p.Collection) && ValidRef(p.Collection!),
        CollectionItemPayload p => Valid(p, p.Collection, p.Media) && ValidRef(p.Collection!) && ValidMedia(p.Media!),
        CollectionReorderPayload p => Valid(p, p.Collection) && ValidRef(p.Collection!) &&
                                      p.Items!.All(item => item != null && Valid(item) && ValidMedia(item)),
        ProfileUpdatePayload p => Valid(p, p.Fields, p.Base) && ValidProfile(p.Fields!),
        _ => Valid(payload)
    };

    // The data annotations of each part (Validator doesn't descend into nested objects)
    private static bool Valid(params object?[] parts) =>
        parts.All(part => part == null || Validator.TryValidateObject(part, new ValidationContext(part), null, validateAllProperties: true));

    private static bool ValidRef(EntityRefDto entity) =>
        entity.Id.HasValue
            ? entity.Id > 0 && entity.Ref == null
            : !string.IsNullOrWhiteSpace(entity.Ref);

    private static bool ValidMedia(MediaKeyDto media) =>
        Enum.IsDefined(media.Type!.Value) && !string.IsNullOrWhiteSpace(media.ExternalApiId);

    private static bool ValidReview(ReviewFieldsDto fields) => Enum.IsDefined(fields.Visibility!.Value);

    private static bool ValidCollection(CollectionFieldsDto fields) => Enum.IsDefined(fields.Visibility!.Value);

    private static bool ValidProfile(ProfileFieldsDto fields) =>
        (fields.Bio != null || fields.ProfilePictureUrl != null) &&
        (string.IsNullOrEmpty(fields.ProfilePictureUrl) || IsWebUrl(fields.ProfilePictureUrl));

    private static bool IsWebUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
