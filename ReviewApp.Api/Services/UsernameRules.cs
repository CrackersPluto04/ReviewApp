using System.Text.RegularExpressions;

namespace ReviewApp.Api.Services;

/// <summary>
/// What a username may look like. Usernames are part of URLs (<c>api/User/{username}</c>, <c>/profile/{username}</c>),
/// so they are limited to characters that never need escaping, and route words such as <c>search</c> and <c>me</c>
/// are reserved. Mirrored by <c>review-app/src/utils/validation.ts</c> and the Android <c>util/Validation.kt</c>;
/// the <c>RenameInvalidUsernames</c> migration applied the same rule to existing accounts.
/// </summary>
public static partial class UsernameRules
{
    public const int MinLength = 3;
    public const int MaxLength = 20;

    /// <summary>Names that collide with routes or could be mistaken for staff. Compared case-insensitively.</summary>
    public static readonly IReadOnlySet<string> Reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "search", "me", "admin", "administrator", "api", "login", "logout", "register", "settings",
        "premium", "support", "system", "root", "null", "undefined", "moderator", "reviewapp"
    };

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex AllowedCharacters();

    /// <summary>Checks a username against the rules.</summary>
    /// <param name="username">The username, already trimmed.</param>
    /// <returns>The error message to show, or null when the username is valid.</returns>
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
