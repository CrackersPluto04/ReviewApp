using ReviewApp.Api.Enums;

namespace ReviewApp.Api.DAL.Entities;

public class SecurityToken
{
    public int ID { get; set; }

    public int UserID { get; set; }
    public User User { get; set; } = null!;

    public SecurityTokenType Type { get; set; }

    // Store the SHA-256 hash of the token/OTP — NEVER the raw token
    public string TokenHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public bool IsUsed { get; set; } = false;

    // Anti-brute force tracking for short OTP codes
    public int FailedAttempts { get; set; } = 0;
}
