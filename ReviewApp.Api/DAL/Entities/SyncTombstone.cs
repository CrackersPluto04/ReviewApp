using ReviewApp.Api.Enums;

namespace ReviewApp.Api.DAL.Entities;

// Records that a user's review or collection was deleted (from any client), so the sync's changes feed can tell
// offline devices to drop their copy. Written in the same SaveChanges as the delete; kept for Sync retention days.
public class SyncTombstone
{
    public int ID { get; set; }

    public int UserID { get; set; }
    public User User { get; set; } = null!;

    // Review or Collection
    public SyncEntityType EntityType { get; set; }
    public int EntityID { get; set; }

    public DateTime DeletedAt { get; set; } = DateTime.UtcNow;

    // Same cursor as Review.RowVersion / Collection.RowVersion
    public long RowVersion { get; set; }
}
