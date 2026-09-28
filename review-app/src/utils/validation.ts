// Shared input rules - keep in sync with the backend DTOs (UserRegisterDto, ChangeEmailDto, ChangePasswordDto)

export const MAX_EMAIL_LENGTH = 254;
export const MIN_PASSWORD_LENGTH = 8;
export const MAX_PASSWORD_LENGTH = 20;

// local@domain.tld - no whitespace, and no empty domain labels (a@b..c, a@.b, a@b.)
const EMAIL_REGEX = /^[^\s@]+@[^\s@.]+(\.[^\s@.]+)+$/;

export function isValidEmail(email: string): boolean {
    return email.length <= MAX_EMAIL_LENGTH && EMAIL_REGEX.test(email);
}
