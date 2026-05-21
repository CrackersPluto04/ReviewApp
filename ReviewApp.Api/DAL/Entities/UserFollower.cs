namespace ReviewApp.Api.DAL.Entities;

public class UserFollower
{
    // The person who clicked the "Follow" button
    public int FollowerID { get; set; }
    public User Follower { get; set; } = null!;

    // The person who is receiving the follow
    public int FollowingID { get; set; }
    public User Following { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
