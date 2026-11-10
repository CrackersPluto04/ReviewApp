namespace ReviewApp.Api.DAL.Entities;

/// <summary>
/// A follow relationship between two users. The key is (<c>FollowerID</c>, <c>FollowingID</c>), so one user can
/// follow another only once. Both foreign keys restrict deletes, to avoid cascade cycles on <see cref="User"/>.
/// </summary>
public class UserFollower
{
    /// <summary>The person who clicked the "Follow" button.</summary>
    public int FollowerID { get; set; }
    public User Follower { get; set; } = null!;

    /// <summary>The person who is receiving the follow.</summary>
    public int FollowingID { get; set; }
    public User Following { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
