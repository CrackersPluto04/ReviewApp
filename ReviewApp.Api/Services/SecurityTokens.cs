using System.Security.Cryptography;
using System.Text;

namespace ReviewApp.Api.Services;

// Generation, hashing and lifetimes of the emailed link tokens and 2FA codes.
// Only the SHA-256 hash is stored (SecurityToken.TokenHash), the raw value exists only in the email.
public static class SecurityTokens
{
    public static readonly TimeSpan EmailVerificationLifetime = TimeSpan.FromHours(24);
    public static readonly TimeSpan EmailChangeLifetime = TimeSpan.FromHours(1);
    public static readonly TimeSpan TwoFactorCodeLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);
    public const int MaxCodeAttempts = 5;
    public const int CodeLength = 6;

    // 256 bits of randomness, base64url so it can go into a link as is
    public static string GenerateLinkToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // Uniformly distributed, keeps leading zeros
    public static string GenerateCode() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    public static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    // Constant-time comparison of a submitted value against a stored hash
    public static bool Matches(string value, string storedHash) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Hash(value)),
            Encoding.ASCII.GetBytes(storedHash));

    public static string Describe(TimeSpan lifetime) =>
        lifetime.TotalHours >= 1
            ? $"{lifetime.TotalHours:0} hour{(lifetime.TotalHours == 1 ? "" : "s")}"
            : $"{lifetime.TotalMinutes:0} minutes";
}
