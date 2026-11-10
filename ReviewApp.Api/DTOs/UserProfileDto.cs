using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DTOs;

/// <summary>
/// A user's public profile, as seen by one particular viewer.
/// </summary>
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
    /// <summary>Whether the viewer follows this user; false for anonymous viewers.</summary>
    public bool IsFollowedByCurrentUser { get; init; }

    /// <summary>The public Premium badge: only this flag, never any subscription details.</summary>
    public bool IsPremium { get; init; }
}
