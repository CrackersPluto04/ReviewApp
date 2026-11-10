using ReviewApp.Api.Enums;

namespace ReviewApp.Api.DAL.Entities;

/// <summary>
/// Records that a user's review or collection was deleted (from any client), so the sync's changes feed can tell
/// offline devices to drop their copy. Written in the same <c>SaveChanges</c> as the delete, so both or neither
/// happen. Kept for the sync retention period (30 days); a device that was away longer gets a full resync instead.
/// </summary>
public class SyncTombstone
{
    public int ID { get; set; }

    public int UserID { get; set; }
    public User User { get; set; } = null!;

    /// <summary>Review or Collection.</summary>
    public SyncEntityType EntityType { get; set; }
    public int EntityID { get; set; }

    public DateTime DeletedAt { get; set; } = DateTime.UtcNow;

    /// <summary>The same cursor as <see cref="Review.RowVersion"/> and <see cref="Collection.RowVersion"/>, so deletions and changes are ordered together in the feed.</summary>
    public long RowVersion { get; set; }
}
