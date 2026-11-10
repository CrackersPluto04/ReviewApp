using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.Enums;

namespace ReviewApp.Api.Services;

// Results of the core write methods of the services (the *CoreAsync ones). They report exactly what happened,
// so the offline sync can tell "already done" from "not allowed" from "missing", while the public methods
// keep the tuples and messages the controllers (and the web app) already use. The core methods take stored
// media IDs and never evaluate achievements; their callers do that after the change is committed.

/// <summary>
/// The editable fields of a review.
/// </summary>
public record ReviewFields(decimal Score, string? ReviewText, string? Pros, string? Cons, VisibilityLevel VisibilityLevel);

/// <summary>
/// What a review core write did.
/// </summary>
public enum ReviewWriteStatus
{
    Created,
    Updated,
    Deleted,
    /// <summary>Create: the user already has a review of this media (<see cref="ReviewWriteResult.Review"/> is that one).</summary>
    AlreadyReviewed,
    /// <summary>Update or delete: no such review of this user.</summary>
    NotFound
}

/// <summary>The outcome of a review core write, with the review it created, changed or found.</summary>
public record ReviewWriteResult(ReviewWriteStatus Status, Review? Review = null);

/// <summary>
/// What a collection core write did.
/// </summary>
public enum CollectionWriteStatus
{
    Created,
    Updated,
    Deleted,
    Added,
    Removed,
    Reordered,
    /// <summary>Add: the media is in the collection already.</summary>
    AlreadyPresent,
    /// <summary>Remove: the media is not in the collection.</summary>
    NotPresent,
    /// <summary>No such collection of this user.</summary>
    NotFound,
    /// <summary>The default Favourites collection cannot be renamed, edited or deleted.</summary>
    Locked,
    /// <summary>Another collection of the user has this name (see <see cref="CollectionWriteResult.ExistingCollectionID"/>).</summary>
    NameTaken,
    /// <summary>The name is blank or too long.</summary>
    InvalidName
}

/// <summary>The outcome of a collection core write.</summary>
/// <param name="Status">What happened.</param>
/// <param name="Collection">The collection that was created, changed or found.</param>
/// <param name="ExistingCollectionID">For <see cref="CollectionWriteStatus.NameTaken"/>: the collection that has the name.</param>
public record CollectionWriteResult(CollectionWriteStatus Status, Collection? Collection = null, int? ExistingCollectionID = null);

/// <summary>
/// What a follow core write did.
/// </summary>
public enum FollowWriteStatus
{
    Changed,
    /// <summary>Already in the requested state.</summary>
    Unchanged,
    /// <summary>Following oneself.</summary>
    Self,
    /// <summary>The user to follow does not exist.</summary>
    TargetMissing
}
