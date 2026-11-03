import { describe, expect, it } from 'vitest';
import {
    isValidEmail, isValidReply, MAX_EMAIL_LENGTH, MAX_PASSWORD_LENGTH, MAX_REPLY_LENGTH, MAX_USERNAME_LENGTH, MIN_PASSWORD_LENGTH,
    MIN_USERNAME_LENGTH, usernameError,
} from '../../src/utils/validation';

describe('validation', () => {
    it('keeps the limits in sync with the backend DTOs', () => {
        expect(MAX_EMAIL_LENGTH).toBe(254);
        expect(MIN_PASSWORD_LENGTH).toBe(8);
        // Above 24 a multi-byte password could pass BCrypt's 72 byte limit (see CLAUDE.md)
        expect(MAX_PASSWORD_LENGTH).toBe(20);
        expect(MAX_REPLY_LENGTH).toBe(500);
        expect(MIN_USERNAME_LENGTH).toBe(3);
        expect(MAX_USERNAME_LENGTH).toBe(20);
    });

    // Same rule as the backend's UsernameRules: usernames are part of profile URLs
    describe('usernameError', () => {
        it.each([
            ['abc', ''],
            ['Ben_2004', ''],
            ['movie-fan', ''],
            ['searcher', ''],
            ['ab', 'Username must be between 3 and 20 characters.'],
            ['a'.repeat(21), 'Username must be between 3 and 20 characters.'],
            ['a/b', "Username can only contain letters, numbers, '_' and '-'."],
            ['dot.name', "Username can only contain letters, numbers, '_' and '-'."],
            ['two words', "Username can only contain letters, numbers, '_' and '-'."],
            ['Bálint', "Username can only contain letters, numbers, '_' and '-'."],
            ['search', 'This username is reserved, please choose another one.'],
            ['ADMIN', 'This username is reserved, please choose another one.'],
        ])('%j -> %j', (username, expected) => {
            expect(usernameError(username)).toBe(expected);
        });
    });

    describe('isValidReply', () => {
        it.each([
            ['a', true],
            ['  hello  ', true],
            ['x'.repeat(MAX_REPLY_LENGTH), true],
            // Trimmed like the backend does, so surrounding whitespace doesn't count towards the limit
            [`  ${'x'.repeat(MAX_REPLY_LENGTH)}  `, true],
            ['x'.repeat(MAX_REPLY_LENGTH + 1), false],
            ['', false],
            ['   \n\t ', false],
        ])('%j -> %s', (content, expected) => {
            expect(isValidReply(content)).toBe(expected);
        });
    });

    describe('isValidEmail', () => {
        it.each([
            'alice@example.com',
            'a.b+tag@sub.example.co.uk',
            'x@y.z',
        ])('accepts %s', email => {
            expect(isValidEmail(email)).toBe(true);
        });

        it.each([
            '',
            'alice',
            'alice@',
            '@example.com',
            'alice@example',
            'alice@@example.com',
            'al ice@example.com',
            'alice@exa mple.com',
            'alice@example..com',
            'alice@.example.com',
            'alice@example.com.',
        ])('rejects %j', email => {
            expect(isValidEmail(email)).toBe(false);
        });

        it('rejects addresses over the maximum length', () => {
            const domain = '@example.com';
            const atLimit = 'a'.repeat(MAX_EMAIL_LENGTH - domain.length) + domain;

            expect(isValidEmail(atLimit)).toBe(true);
            expect(isValidEmail('a' + atLimit)).toBe(false);
        });
    });
});
