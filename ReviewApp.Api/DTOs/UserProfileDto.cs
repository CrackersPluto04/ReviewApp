using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DTOs;

public record UserProfileDto
{
    public int ID { get; init; }
    [Required, MaxLength(20)]
    public string Username { get; init; } = string.Empty;
    [MaxLength(150)]
    public string? Bio { get; set; }
    public string? ProfilePictureUrl { get; init; }
    public string CreatedAt { get; set; } = string.Empty;

    public int FollowersCount { get; init; }
    public int FollowingCount { get; init; }
    public bool IsFollowedByCurrentUser { get; init; }
}
