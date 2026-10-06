// Shared input rules - keep in sync with the backend DTOs (UserRegisterDto, ChangeEmailDto, ChangePasswordDto, CreateReplyDto)

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
