// Shared input rules - keep in sync with the backend DTOs (UserRegisterDto, ChangeEmailDto, ChangePasswordDto)

export const MAX_EMAIL_LENGTH = 254;
export const MIN_PASSWORD_LENGTH = 8;
// BCrypt only uses the first 72 bytes of a password
export const MAX_PASSWORD_LENGTH = 72;

// local@domain.tld - no whitespace, and no empty domain labels (a@b..c, a@.b, a@b.)
const EMAIL_REGEX = /^[^\s@]+@[^\s@.]+(\.[^\s@.]+)+$/;

export function isValidEmail(email: string): boolean {
    return email.length <= MAX_EMAIL_LENGTH && EMAIL_REGEX.test(email);
}
