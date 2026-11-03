// Shared input rules - keep in sync with the backend DTOs (UserRegisterDto, ChangeEmailDto, ChangePasswordDto, CreateReplyDto)
// and UsernameRules

export const MIN_USERNAME_LENGTH = 3;
export const MAX_USERNAME_LENGTH = 20;

// Usernames are part of URLs: only characters that never need escaping, and no route words
const USERNAME_REGEX = /^[A-Za-z0-9_-]+$/;
const RESERVED_USERNAMES = new Set([
    'search', 'me', 'admin', 'administrator', 'api', 'login', 'logout', 'register', 'settings',
    'premium', 'support', 'system', 'root', 'null', 'undefined', 'moderator', 'reviewapp'
]);

// The error to show for an (already trimmed) username, or '' when it is valid
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
export const MAX_PASSWORD_LENGTH = 20;
export const MAX_REPLY_LENGTH = 500;

// Replies are trimmed on the backend, so whitespace-only text counts as empty
export function isValidReply(content: string): boolean {
    const trimmed = content.trim();
    return trimmed.length > 0 && trimmed.length <= MAX_REPLY_LENGTH;
}

// local@domain.tld - no whitespace, and no empty domain labels (a@b..c, a@.b, a@b.)
const EMAIL_REGEX = /^[^\s@]+@[^\s@.]+(\.[^\s@.]+)+$/;

export function isValidEmail(email: string): boolean {
    return email.length <= MAX_EMAIL_LENGTH && EMAIL_REGEX.test(email);
}
