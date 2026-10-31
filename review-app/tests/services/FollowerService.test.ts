import { describe, expect } from 'vitest';
import { followerService } from '../../src/services/FollowerService';
import { server } from '../support/server';
import { capture, networkError, ok } from '../support/api';
import { muteConsoleError } from '../support/browser';
import { knownIssue } from '../support/known-issue';
import { describeEndpoint } from './endpoint';

describe('FollowerService', () => {
    describeEndpoint({
        name: 'follow',
        method: 'post',
        path: 'Follower/12/follow',
        call: () => followerService.follow(12),
        response: ok({ message: 'You now follow bob.' }),
        result: { success: true, message: 'You now follow bob.' },
        fallback: 'Something went wrong while trying to follow.',
        network: 'Network error while reaching trying to follow.',
    });

    describeEndpoint({
        name: 'unfollow',
        method: 'delete',
        path: 'Follower/12/unfollow',
        call: () => followerService.unfollow(12),
        response: ok({ message: 'Unfollowed.' }),
        result: { success: true, message: 'Unfollowed.' },
        fallback: 'Something went wrong while trying to unfollow.',
        network: 'Network error while reaching trying to unfollow.',
    });

    describeEndpoint({
        name: 'remove',
        method: 'delete',
        path: 'Follower/12/remove',
        call: () => followerService.remove(12),
        response: ok({ message: 'Removed.' }),
        result: { success: true, message: 'Removed.' },
        fallback: 'Something went wrong while trying to remove follower.',
        network: 'Network error while reaching trying to remove follower.',
    });

    knownIssue('W3', 'the network messages read as a sentence', async () => {
        muteConsoleError();
        server.use(capture('post', 'Follower/12/follow', networkError()).handler);

        const result = await followerService.follow(12);

        // "Network error while reaching trying to follow." mixes two phrasings
        expect(result.message).not.toContain('reaching trying');
    });

    knownIssue('W3', 'unfollow and remove log their own action', async () => {
        const consoleError = muteConsoleError();
        server.use(capture('delete', 'Follower/12/remove', networkError()).handler);

        await followerService.remove(12);

        // Every method logs "Follow error:"
        expect(consoleError.mock.calls[0][0]).toBe('Remove follower error:');
    });
});
