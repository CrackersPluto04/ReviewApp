using System.Security.Cryptography;
using System.Text;

namespace ReviewApp.Api.Services;

/// <summary>
/// Generation, hashing and lifetimes of the emailed link tokens and two-factor codes. Only the SHA-256 hash is
/// stored (<c>SecurityToken.TokenHash</c>); the raw value exists only in the email, so a database leak cannot be
/// used to confirm an email or log in.
/// </summary>
public static class SecurityTokens
{
    /// <summary>Verification links are valid for a day: the user may open the email much later.</summary>
    public static readonly TimeSpan EmailVerificationLifetime = TimeSpan.FromHours(24);
    /// <summary>Email change links are short-lived because they can take over the account's address.</summary>
    public static readonly TimeSpan EmailChangeLifetime = TimeSpan.FromHours(1);
    public static readonly TimeSpan TwoFactorCodeLifetime = TimeSpan.FromMinutes(10);
    /// <summary>Minimum time between two emails of the same kind to the same user.</summary>
    public static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);
    /// <summary>Wrong guesses after which a code is burned.</summary>
    public const int MaxCodeAttempts = 5;
    public const int CodeLength = 6;

    /// <summary>Generates a link token with 256 bits of randomness, base64url encoded so it can go into a link as is.</summary>
    public static string GenerateLinkToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Generates a uniformly distributed six-digit code, keeping leading zeros.</summary>
    public static string GenerateCode() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    /// <summary>Hashes a token or code for storage.</summary>
    public static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>Compares a submitted value against a stored hash in constant time, so timing reveals nothing about the hash.</summary>
    public static bool Matches(string value, string storedHash) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Hash(value)),
            Encoding.ASCII.GetBytes(storedHash));

    /// <summary>Formats a lifetime for emails, e.g. "24 hours" or "10 minutes".</summary>
    public static string Describe(TimeSpan lifetime) =>
        lifetime.TotalHours >= 1
            ? $"{lifetime.TotalHours:0} hour{(lifetime.TotalHours == 1 ? "" : "s")}"
            : $"{lifetime.TotalMinutes:0} minutes";
}
