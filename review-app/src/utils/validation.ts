/**
 * @module validation
 * Shared input rules. Keep in sync with the backend DTOs (`UserRegisterDto`, `ChangeEmailDto`,
 * `ChangePasswordDto`, `CreateReplyDto`) and `UsernameRules`; the Android app mirrors them too. The checks only
 * save a round trip: the backend validates every request again.
 */

export const MIN_USERNAME_LENGTH = 3;
export const MAX_USERNAME_LENGTH = 20;

// Usernames are part of URLs: only characters that never need escaping, and no route words
const USERNAME_REGEX = /^[A-Za-z0-9_-]+$/;
const RESERVED_USERNAMES = new Set([
    'search', 'me', 'admin', 'administrator', 'api', 'login', 'logout', 'register', 'settings',
    'premium', 'support', 'system', 'root', 'null', 'undefined', 'moderator', 'reviewapp'
]);

/**
 * Checks a username against the length, character and reserved word rules.
 * @param username The username, already trimmed.
 * @returns The error message to show, or an empty string when the username is valid.
 */
export function usernameError(username: string): string {
    if (username.length < MIN_USERNAME_LENGTH || username.length > MAX_USERNAME_LENGTH)
        return `Username must be between ${MIN_USERNAME_LENGTH} and ${MAX_USERNAME_LENGTH} characters.`;
    if (!USERNAME_REGEX.test(username))
        return "Username can only contain letters, numbers, '_' and '-'.";
    if (RESERVED_USERNAMES.has(username.toLowerCase()))
        return 'This username is reserved, please choose another one.';
    return '';
}

export const MAX_EMAIL_LENGTH = 254;
export const MIN_PASSWORD_LENGTH = 8;
/**
 * Capped so a password stays under BCrypt's 72 byte limit even with multi-byte characters (JS and C# both count
 * UTF-16 units, each at most 3 UTF-8 bytes), which makes a separate byte check unnecessary. Don't raise it above 24
 * without adding one.
 */
export const MAX_PASSWORD_LENGTH = 20;
export const MAX_REPLY_LENGTH = 500;

/**
 * Whether reply text can be posted. Replies are trimmed on the backend, so whitespace-only text counts as empty.
 * @param content The text as typed.
 */
export function isValidReply(content: string): boolean {
    const trimmed = content.trim();
    return trimmed.length > 0 && trimmed.length <= MAX_REPLY_LENGTH;
}

// local@domain.tld - no whitespace, and no empty domain labels (a@b..c, a@.b, a@b.)
const EMAIL_REGEX = /^[^\s@]+@[^\s@.]+(\.[^\s@.]+)+$/;

/**
 * Whether a string looks like an email address: a deliberately loose shape check, since only a delivered email
 * proves an address.
 * @param email The address, already trimmed.
 */
export function isValidEmail(email: string): boolean {
    return email.length <= MAX_EMAIL_LENGTH && EMAIL_REGEX.test(email);
}
