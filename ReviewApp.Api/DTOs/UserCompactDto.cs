namespace ReviewApp.Api.DTOs;

/// <summary>
/// The short form of a user used in follower lists, search results and the sync account data.
/// </summary>
public record UserCompactDto
{
    public int ID { get; init; }
    public string Username { get; init; } = string.Empty;
    public string? ProfilePictureUrl { get; init; }

    /// <summary>Whether the viewer follows this user. Null where the follow state is not computed.</summary>
    public bool? IsFollowedByCurrentUser { get; init; }
}
