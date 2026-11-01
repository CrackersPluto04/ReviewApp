import { describe } from 'vitest';
import { achievementService } from '../../src/services/AchievementService';
import { ok } from '../support/api';
import { achievement } from '../support/builders';
import { describeEndpoint } from './endpoint';

describe('AchievementService', () => {
    const list = [achievement({ title: 'Critic' })];

    describeEndpoint({
        name: 'getUserAchievements',
        method: 'get',
        path: 'Achievement/alice',
        call: () => achievementService.getUserAchievements('alice'),
        response: ok(list),
        result: { success: true, data: list },
        fallback: 'Something went wrong while getting achievements.',
        network: 'Network error while reaching achievements.',
    });

    describeEndpoint({
        name: 'getUserAchievements (username is escaped)',
        method: 'get',
        path: 'Achievement/a%20b%2Fc',
        call: () => achievementService.getUserAchievements('a b/c'),
        response: ok([]),
        result: { success: true, data: [] },
        fallback: 'Something went wrong while getting achievements.',
        network: 'Network error while reaching achievements.',
    });
});
