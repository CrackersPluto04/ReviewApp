using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace ReviewApp.Api.Services.Sync;

/// <summary>
/// The kinds of offline changes the sync accepts (<see cref="SyncOpDto.Type"/>).
/// </summary>
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

    /// <summary>Whether the op creates something, so it may carry a <c>clientRef</c>.</summary>
    public static bool Creates(string type) => type is ReviewCreate or CollectionCreate;
}

// Payloads. Nullable members with [Required] make a missing field a validation error instead of a silent default.

/// <summary>
/// A review or collection: its real ID, or the <c>clientRef</c> of the op that created it on the device, for
/// something created offline whose real ID the device does not know yet. Exactly one of the two is set.
/// </summary>
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

/// <summary>The editable profile fields. A field left out (null) is not changed; an empty string clears it.</summary>
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
    /// <summary>When the user wrote it. Used only within the bounds of <see cref="ClientTimestamp"/>.</summary>
    public DateTimeOffset? CreatedAt { get; init; }
}

/// <summary>Payload of <c>review.update</c>.</summary>
public record ReviewUpdatePayload
{
    [Required]
    public EntityRefDto? Review { get; init; }
    /// <summary>The server version the edit started from. Equal to the current one means nobody else changed it, so no merge is needed.</summary>
    public long? BaseVersion { get; init; }
    /// <summary>The values the device's edit started from. Null means the device's values win everywhere.</summary>
    public ReviewFieldsDto? Base { get; init; }
    [Required]
    public ReviewFieldsDto? Fields { get; init; }
}

/// <summary>Payload of <c>review.delete</c>.</summary>
public record ReviewDeletePayload
{
    [Required]
    public EntityRefDto? Review { get; init; }
    /// <summary>The version the user saw when deleting. If the review changed since, it is a conflict (<c>modified_since</c>); null deletes whatever is there.</summary>
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

/// <summary>Payload of <c>collection.reorder</c>: the device's order of the items (all it knew of). Items it did not know keep their place after them.</summary>
public record CollectionReorderPayload
{
    [Required]
    public EntityRefDto? Collection { get; init; }
    [Required, MaxLength(1000)]
    public List<MediaKeyDto>? Items { get; init; }
}

/// <summary>Payload of <c>profile.update</c>. The latest change wins; <c>Base</c> lets the result name what was overwritten.</summary>
public record ProfileUpdatePayload
{
    [Required]
    public ProfileFieldsDto? Fields { get; init; }
    /// <summary>The values the device had seen, so the result can mention what was overwritten.</summary>
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

/// <summary>Payload of <c>notification.markAllSeen</c> and <c>notification.deleteSeen</c>: only up to the newest notification the device had, so ones it never saw are untouched.</summary>
public record NotificationUpToPayload
{
    [Required, Range(1, int.MaxValue)]
    public int? UpToId { get; init; }
}

/// <summary>The editable fields of a collection.</summary>
public record CollectionFields(string Name, VisibilityLevel Visibility);

/// <summary>An op whose payload parsed and validated: its type and the typed payload.</summary>
public record ParsedSyncOp(string Type, object Payload);

/// <summary>
/// Turns an op's JSON payload into its typed payload, checking every field. The controller's model validation does
/// not reach into the payload, so this is the only validation the payload gets. An invalid op is rejected without
/// touching anything.
/// </summary>
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

    /// <summary>Whether the op type is one of <see cref="SyncOpTypes"/>.</summary>
    public static bool IsKnownType(string? type) => type != null && PayloadTypes.ContainsKey(type);

    /// <summary>Deserializes and validates an op's payload.</summary>
    /// <param name="type">The op type.</param>
    /// <param name="payload">The op's JSON payload.</param>
    /// <param name="clientRef">The op's client reference; only creates may have one.</param>
    /// <returns>The parsed op, or null when the type is unknown, the payload is not a valid object of that type, or the reference is not allowed.</returns>
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

    // Validates the data annotations of each part separately, because Validator does not descend into nested objects
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
