using ReviewApp.Api.Enums;
using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DAL.Entities;

public class SecurityToken
{
    public int ID { get; set; }

    public int UserID { get; set; }
    public User User { get; set; } = null!;

    public SecurityTokenType Type { get; set; }

    // SHA-256 hash (hex) of the link token / code - NEVER the raw value
    [Required, MaxLength(64)]
    public string TokenHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public bool IsUsed { get; set; } = false;

    // Anti-brute force tracking for short OTP codes
    public int FailedAttempts { get; set; } = 0;
}
