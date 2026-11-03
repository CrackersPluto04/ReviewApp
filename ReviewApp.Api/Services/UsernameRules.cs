using System.Text.RegularExpressions;

namespace ReviewApp.Api.Services;

// What a username may look like. Usernames are part of URLs (api/User/{username}, /profile/{username}), so they are
// limited to characters that never need escaping, and route words (search, me, ...) are reserved.
// Mirrored by review-app/src/utils/validation.ts and the Android util/Validation.kt; the rename migration
// (RenameInvalidUsernames) applies the same rule to existing accounts.
public static partial class UsernameRules
{
    public const int MinLength = 3;
    public const int MaxLength = 20;

    public static readonly IReadOnlySet<string> Reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "search", "me", "admin", "administrator", "api", "login", "logout", "register", "settings",
        "premium", "support", "system", "root", "null", "undefined", "moderator", "reviewapp"
    };

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex AllowedCharacters();

    // The error to show, or null when the (already trimmed) username is valid
    public static string? Validate(string username)
    {
        if (username.Length < MinLength || username.Length > MaxLength)
            return $"Username must be between {MinLength} and {MaxLength} characters.";
        if (!AllowedCharacters().IsMatch(username))
            return "Username can only contain letters, numbers, '_' and '-'.";
        if (Reserved.Contains(username))
            return "This username is reserved, please choose another one.";
        return null;
    }
}
