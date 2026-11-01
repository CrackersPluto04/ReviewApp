import { describe, expect, it, vi } from 'vitest';
import { screen } from '@testing-library/preact';
import { ReviewCard } from '../../src/components/ReviewCard';
import { FOLLOWERS_ONLY, PRIVATE, PUBLIC, profileReview, review } from '../support/builders';
import { currentLocation, renderWithApp } from '../support/render';

describe('ReviewCard', () => {
    describe('media mode (a media\'s review list)', () => {
        it('shows the author, the score and its verdict', () => {
            renderWithApp(<ReviewCard rev={review({ username: 'bob', score: 7, reviewText: 'Good.', pros: 'Cast', cons: 'Length' })} />);

            expect(screen.getByRole('button', { name: /bob/ })).toBeInTheDocument();
            expect(screen.getByText('7/10')).toBeInTheDocument();
            expect(screen.getByText('Great')).toBeInTheDocument();
            expect(screen.getByText('Good.')).toBeInTheDocument();
            expect(screen.getByText('Cast')).toBeInTheDocument();
            expect(screen.getByText('Length')).toBeInTheDocument();
            expect(screen.queryByRole('button', { name: 'Edit Review' })).not.toBeInTheDocument();
        });

        it.each([
            [10, 'Great'], [7, 'Great'], [6.5, 'Mixed'], [4, 'Mixed'], [3.5, 'Poor'], [1, 'Poor'],
        ])('calls a %s "%s"', (score, verdict) => {
            renderWithApp(<ReviewCard rev={review({ score })} />);
            expect(screen.getByText(verdict)).toBeInTheDocument();
        });

        it('shows dashes for missing pros and cons', () => {
            renderWithApp(<ReviewCard rev={review({ reviewText: '', pros: null, cons: null })} />);

            expect(screen.getByText('No written review.')).toBeInTheDocument();
            expect(screen.getAllByText('-')).toHaveLength(2);
        });

        it('opens the author\'s profile', async () => {
            const { user } = renderWithApp(<ReviewCard rev={review({ username: 'b b' })} />);

            await user.click(screen.getByRole('button', { name: /b b/ }));

            expect(currentLocation()).toBe('/profile/b%20b');
        });

        it('shows the reply count and lets anyone open the thread (the list only has public reviews)', () => {
            renderWithApp(<ReviewCard rev={review({ replyCount: 3 })} />);

            expect(screen.getByRole('button', { name: 'Replies (3)' })).toBeInTheDocument();
        });
    });

    describe('profile mode', () => {
        it('shows the media and the dates instead of the author', async () => {
            const rev = profileReview({ title: 'Arrival', mediaType: 1, externalApiID: 'tmdb-9', createdAt: '2026-03-10', updatedAt: '2026-03-12' });
            const { user } = renderWithApp(<ReviewCard rev={rev} mode="profile" />);

            expect(screen.getByText('Reviewed: 2026-03-10')).toBeInTheDocument();
            expect(screen.getByText('Modified: 2026-03-12')).toBeInTheDocument();
            expect(screen.getByRole('img', { name: 'Arrival' })).toHaveAttribute('src', rev.posterUrl);
            expect(screen.queryByRole('button', { name: /bob/ })).not.toBeInTheDocument();

            await user.click(screen.getByText('Arrival'));
            expect(currentLocation()).toBe('/media/1/tmdb-9?tab=details');
        });

        it('opens the media from its poster', async () => {
            const { user } = renderWithApp(<ReviewCard rev={profileReview({ title: 'Arrival', posterUrl: null, externalApiID: 'x' })} mode="profile" />);

            expect(screen.getByRole('img', { name: 'Arrival' }).getAttribute('src')).toContain('placehold.co');
            await user.click(screen.getByRole('img', { name: 'Arrival' }));
            expect(currentLocation()).toBe('/media/0/x?tab=details');
        });

        it('hides the visibility and the owner actions from other users', () => {
            renderWithApp(<ReviewCard rev={profileReview({ isOwner: false })} mode="profile" />);

            expect(screen.queryByText('Public')).not.toBeInTheDocument();
            expect(screen.queryByRole('button', { name: 'Edit Review' })).not.toBeInTheDocument();
            expect(screen.queryByRole('button', { name: 'Delete Review' })).not.toBeInTheDocument();
        });

        it.each([
            [PRIVATE, 'Private'], [PUBLIC, 'Public'], [FOLLOWERS_ONLY, 'Followers'],
        ])('shows the owner the visibility %s as %s', (visibilityLevel, label) => {
            renderWithApp(<ReviewCard rev={profileReview({ isOwner: true, visibilityLevel })} mode="profile" />);
            expect(screen.getByText(label)).toBeInTheDocument();
        });

        it('lets the owner edit and delete', async () => {
            const onDelete = vi.fn();
            const rev = profileReview({ id: 44, isOwner: true, mediaType: 2, externalApiID: 'sp-1' });
            const { user } = renderWithApp(<ReviewCard rev={rev} mode="profile" onDelete={onDelete} />);

            await user.click(screen.getByRole('button', { name: 'Delete Review' }));
            expect(onDelete).toHaveBeenCalledWith(44);

            await user.click(screen.getByRole('button', { name: 'Edit Review' }));
            expect(currentLocation()).toBe('/media/2/sp-1?tab=review');
        });

        it('shows the score in its own column', () => {
            renderWithApp(<ReviewCard rev={profileReview({ score: 4.5 })} mode="profile" />);

            expect(screen.getByText('4.5/10')).toBeInTheDocument();
            expect(screen.getByText('Mixed')).toBeInTheDocument();
        });
    });
});
