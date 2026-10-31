import { describe, expect, it } from 'vitest';
import { userService } from '../../src/services/UserService';
import { server } from '../support/server';
import { apiError, capture, networkError, ok } from '../support/api';
import { compactUser, review, userProfile, collection } from '../support/builders';
import { muteConsoleError } from '../support/browser';
import { describeEndpoint } from './endpoint';

const accountChange = {
    safeErrorBody: true,
    readsValidationProblems: true,
    explainsUnauthorized: true,
};

describe('UserService', () => {
    describe('searchUsers', () => {
        it('searches with the escaped query', async () => {
            const users = [compactUser()];
            const endpoint = capture('get', 'User/search', ok(users));
            server.use(endpoint.handler);

            expect(await userService.searchUsers('a&b c')).toEqual({ success: true, data: users });
            expect(endpoint.last.url.searchParams.get('q')).toBe('a&b c');
            expect(endpoint.last.credentials).toBe('include');
        });

        it('returns an empty list on failure', async () => {
            muteConsoleError();
            server.use(capture('get', 'User/search', apiError(500, 'Down')).handler);
            expect(await userService.searchUsers('x')).toEqual({ success: false, data: [] });

            server.use(capture('get', 'User/search', networkError()).handler);
            expect(await userService.searchUsers('x')).toEqual({ success: false, data: [] });
        });
    });

    const profile = userProfile({ username: 'bob' });

    describeEndpoint({
        name: 'getUserProfile',
        method: 'get',
        path: 'User/b%20b',
        call: () => userService.getUserProfile('b b'),
        response: ok(profile),
        result: { success: true, data: profile },
        fallback: 'Something went wrong while getting user profile infos.',
        network: 'Network error while reaching user profile infos.',
    });

    describeEndpoint({
        name: 'updateMyProfile',
        method: 'patch',
        path: 'User/me',
        call: () => userService.updateMyProfile('New bio', 'https://img.example/me.png'),
        request: req => expect(req.body).toEqual({ bio: 'New bio', profilePictureUrl: 'https://img.example/me.png' }),
        response: ok({ message: 'Profile updated.' }),
        result: { success: true, message: 'Profile updated.' },
        fallback: 'Something went wrong while updating user profile.',
        network: 'Network error while updating user profile.',
    });

    describeEndpoint({
        name: 'updateMyProfile (bio only)',
        method: 'patch',
        path: 'User/me',
        call: () => userService.updateMyProfile('Only bio'),
        // An undefined picture is left out, so the backend keeps the current one
        request: req => expect(req.body).toEqual({ bio: 'Only bio' }),
        response: ok({ message: 'Profile updated.' }),
        result: { success: true, message: 'Profile updated.' },
        fallback: 'Something went wrong while updating user profile.',
        network: 'Network error while updating user profile.',
    });

    describeEndpoint({
        ...accountChange,
        name: 'changeUsername',
        method: 'patch',
        path: 'User/me',
        call: () => userService.changeUsername('newname'),
        request: req => expect(req.body).toEqual({ username: 'newname' }),
        response: ok({ message: 'Username changed.' }),
        result: { success: true, message: 'Username changed.' },
        fallback: 'Something went wrong while changing username.',
        network: 'Network error while changing username.',
    });

    describeEndpoint({
        ...accountChange,
        name: 'changeEmail',
        method: 'put',
        path: 'User/me/email',
        call: () => userService.changeEmail('new@example.com', 'hunter22'),
        request: req => expect(req.body).toEqual({ newEmail: 'new@example.com', currentPassword: 'hunter22' }),
        response: ok({ message: 'Check your inbox.' }),
        result: { success: true, message: 'Check your inbox.' },
        fallback: 'Something went wrong while changing email.',
        network: 'Network error while changing email.',
    });

    describeEndpoint({
        ...accountChange,
        name: 'changePassword',
        method: 'put',
        path: 'User/me/password',
        call: () => userService.changePassword('old-pass1', 'new-pass1'),
        request: req => expect(req.body).toEqual({ currentPassword: 'old-pass1', newPassword: 'new-pass1' }),
        response: ok({ message: 'Password changed.' }),
        result: { success: true, message: 'Password changed.' },
        fallback: 'Something went wrong while changing password.',
        network: 'Network error while changing password.',
    });

    const collections = [collection()];

    describeEndpoint({
        name: 'getUserCollections',
        method: 'get',
        path: 'User/bob/collections',
        call: () => userService.getUserCollections('bob'),
        request: req => expect(req.url.searchParams.get('sortBy')).toBe('createdAt_desc'),
        response: ok(collections),
        result: { success: true, data: collections },
        fallback: 'Something went wrong while getting user collections.',
        network: 'Network error while reaching user collections.',
    });

    describeEndpoint({
        name: 'getUserCollections (sorted)',
        method: 'get',
        path: 'User/bob/collections',
        call: () => userService.getUserCollections('bob', 'name_asc'),
        request: req => expect(req.url.searchParams.get('sortBy')).toBe('name_asc'),
        response: ok(collections),
        result: { success: true, data: collections },
        fallback: 'Something went wrong while getting user collections.',
        network: 'Network error while reaching user collections.',
    });

    const reviews = { items: [review()], totalPages: 1 };

    describeEndpoint({
        name: 'getUserReviews',
        method: 'get',
        path: 'User/bob/reviews',
        call: () => userService.getUserReviews('bob', { page: 2, sortBy: 'score_desc', minScore: 0, maxScore: 10, hasWrittenText: true }),
        request: req => expect(Object.fromEntries(req.url.searchParams)).toEqual({
            page: '2', sortBy: 'score_desc', minScore: '0', maxScore: '10', hasWrittenText: 'true',
        }),
        response: ok(reviews),
        result: { success: true, data: reviews },
        fallback: 'Something went wrong while getting user reviews.',
        network: 'Network error while reaching user reviews.',
    });

    describeEndpoint({
        name: 'getUserReviews (without text filter)',
        method: 'get',
        path: 'User/bob/reviews',
        call: () => userService.getUserReviews('bob', { page: 1, sortBy: '', minScore: 2, maxScore: 8, hasWrittenText: false }),
        // An empty sort and a false text filter are left out
        request: req => expect(Object.fromEntries(req.url.searchParams)).toEqual({ page: '1', minScore: '2', maxScore: '8' }),
        response: ok(reviews),
        result: { success: true, data: reviews },
        fallback: 'Something went wrong while getting user reviews.',
        network: 'Network error while reaching user reviews.',
    });

    const users = [compactUser()];

    describeEndpoint({
        name: 'getUserFollowers',
        method: 'get',
        path: 'User/bob/followers',
        call: () => userService.getUserFollowers('bob'),
        response: ok(users),
        result: { success: true, data: users },
        fallback: 'Something went wrong while getting user followers.',
        network: 'Network error while reaching user followers.',
    });

    describeEndpoint({
        name: 'getUserFollowing',
        method: 'get',
        path: 'User/bob/following',
        call: () => userService.getUserFollowing('bob'),
        response: ok(users),
        result: { success: true, data: users },
        fallback: 'Something went wrong while getting user following.',
        network: 'Network error while reaching user following.',
    });
});
