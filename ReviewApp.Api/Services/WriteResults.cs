using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.Enums;

namespace ReviewApp.Api.Services;

/* Results of the core write methods of the services (the *CoreAsync ones). They report exactly what happened,
   so the offline sync can tell "already done" from "not allowed" from "missing", while the public methods
   keep the tuples and messages the controllers (and the web app) already use. The core methods take stored
   media IDs and never evaluate achievements; their callers do that after the change is committed. */

// The editable fields of a review
public record ReviewFields(decimal Score, string? ReviewText, string? Pros, string? Cons, VisibilityLevel VisibilityLevel);

public enum ReviewWriteStatus
{
    Created,
    Updated,
    Deleted,
    // Create: the user already has a review of this media (Review is that one)
    AlreadyReviewed,
    // Update / delete: no such review of this user
    NotFound
}

public record ReviewWriteResult(ReviewWriteStatus Status, Review? Review = null);

public enum CollectionWriteStatus
{
    Created,
    Updated,
    Deleted,
    Added,
    Removed,
    Reordered,
    // Add: the media is in the collection already
    AlreadyPresent,
    // Remove: the media isn't in the collection
    NotPresent,
    // No such collection of this user
    NotFound,
    // The default Favourites collection can't be renamed, edited or deleted
    Locked,
    // Another collection of the user has this name (ExistingCollectionID)
    NameTaken,
    // Blank or too long
    InvalidName
}

public record CollectionWriteResult(CollectionWriteStatus Status, Collection? Collection = null, int? ExistingCollectionID = null);

public enum FollowWriteStatus
{
    Changed,
    // Already in the requested state
    Unchanged,
    // Following yourself
    Self,
    // The user to follow doesn't exist
    TargetMissing
}
