using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DAL.Entities;

/// <summary>
/// A registered user. A database trigger (<c>AddDefaultCollection</c>) creates the user's Favourites collection on
/// insert. Username and email are unique.
/// </summary>
public class User
{
    public int ID { get; set; }

    /// <summary>Part of profile URLs, so restricted by <c>UsernameRules</c>.</summary>
    [Required, MaxLength(20)]
    public string Username { get; set; } = string.Empty;
    [Required, MaxLength(254)]
    public string Email { get; set; } = string.Empty;
    /// <summary>BCrypt hash.</summary>
    [Required]
    public string PasswordHash { get; set; } = string.Empty;
    /// <summary>Included in the JWT; bumping it invalidates every previously issued token, which is how sessions are revoked.</summary>
    public int TokenVersion { get; set; }

    [MaxLength(150)]
    public string? Bio { get; set; }
    public string? ProfilePictureUrl { get; set; }

    public bool IsEmailConfirmed { get; set; } = false;
    public bool IsTwoFactorEnabled { get; set; } = false;
    /// <summary>The requested new address. <see cref="Email"/> only changes once the link sent here is confirmed.</summary>
    [MaxLength(254)]
    public string? PendingEmail { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>The follow rows where this user is the one being followed.</summary>
    public ICollection<UserFollower> Followers { get; set; } = [];
    /// <summary>The follow rows where this user is the follower.</summary>
    public ICollection<UserFollower> Following { get; set; } = [];
}
