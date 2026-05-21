namespace ReviewApp.Api.DTOs;

public record UserFollowDto
{
    public int ID { get; init; }
    public string Username { get; init; } = string.Empty;
    public string? ProfilePictureUrl { get; init; }

    public bool IsFollowedByCurrentUser { get; init; }
}
