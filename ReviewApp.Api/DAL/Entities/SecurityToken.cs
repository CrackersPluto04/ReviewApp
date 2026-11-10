using ReviewApp.Api.Enums;
using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DAL.Entities;

/// <summary>
/// A single-purpose link token or short code for email verification, an email change or two-factor authentication.
/// Only the hash is stored, so a database leak cannot be used to confirm anything.
/// </summary>
public class SecurityToken
{
    public int ID { get; set; }

    public int UserID { get; set; }
    public User User { get; set; } = null!;

    public SecurityTokenType Type { get; set; }

    /// <summary>SHA-256 hash (hex) of the link token or code. Never the raw value.</summary>
    [Required, MaxLength(64)]
    public string TokenHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    /// <summary>Set when the token is consumed, or invalidated by a newer one of the same user and type, or burned by too many wrong tries.</summary>
    public bool IsUsed { get; set; } = false;

    /// <summary>Wrong guesses so far; the anti-brute-force counter for the short codes.</summary>
    public int FailedAttempts { get; set; } = 0;
}
