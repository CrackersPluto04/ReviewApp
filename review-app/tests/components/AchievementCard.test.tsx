import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/preact';
import { AchievementCard } from '../../src/components/AchievementCard';
import { achievement } from '../support/builders';
import { setupUser } from '../support/render';

// Tiers at 1 / 10 / 50 (see the builder)
const card = () => screen.getByRole('button');

describe('AchievementCard', () => {
    it('shows the icon, medals and title on the front', () => {
        render(<AchievementCard achievement={achievement({ title: 'Critic', iconKey: 'critic', currentProgress: 4 })} />);

        expect(card()).toHaveAccessibleName('Critic, 1 of 3 tiers unlocked. Show details');
        expect(card()).toHaveAttribute('aria-pressed', 'false');
        expect(document.querySelector('img')).toHaveAttribute('src', '/achievements/critic.svg');
        expect(screen.getByLabelText('Bronze earned')).toBeInTheDocument();
        expect(screen.getByLabelText('Silver not earned')).toBeInTheDocument();
    });

    it('flips to the details on click and back', async () => {
        const user = setupUser();
        render(<AchievementCard achievement={achievement({ title: 'Critic', currentProgress: 4 })} />);

        await user.click(card());

        expect(card()).toHaveAttribute('aria-pressed', 'true');
        expect(card()).toHaveAccessibleName('Critic, 1 of 3 tiers unlocked. Hide details');
        const [front, back] = card().querySelectorAll('.MuiPaper-root');
        expect(front).toHaveAttribute('aria-hidden', 'true');
        expect(back).toHaveAttribute('aria-hidden', 'false');

        await user.click(card());
        expect(card()).toHaveAttribute('aria-pressed', 'false');
    });

    it('flips with Enter and Space', async () => {
        const user = setupUser();
        render(<AchievementCard achievement={achievement()} />);

        card().focus();
        await user.keyboard('{Enter}');
        expect(card()).toHaveAttribute('aria-pressed', 'true');
        await user.keyboard(' ');
        expect(card()).toHaveAttribute('aria-pressed', 'false');
        await user.keyboard('a');
        expect(card()).toHaveAttribute('aria-pressed', 'false');
    });

    it('shows the next tier\'s goal and progress on the back', () => {
        render(<AchievementCard achievement={achievement({ currentProgress: 4 })} />);

        expect(screen.getByText('Write 10 reviews')).toBeInTheDocument();
        expect(screen.getByText('Unlock:')).toHaveTextContent('Unlock: 1/3');
        expect(screen.getByText(/Next tier \(Silver\)/)).toHaveTextContent('Next tier (Silver): 4/10');
        expect(screen.getByRole('progressbar', { hidden: true })).toHaveAttribute('aria-valuenow', '40');
    });

    it('shows a completed achievement with its last goal', () => {
        render(<AchievementCard achievement={achievement({ currentProgress: 75 })} />);

        expect(screen.getByText('Completed')).toBeInTheDocument();
        expect(screen.getByText('Write 50 reviews')).toBeInTheDocument();
        expect(screen.queryByText(/Next tier/)).not.toBeInTheDocument();
    });

    it('greys out a locked achievement', () => {
        render(<AchievementCard achievement={achievement({ currentProgress: 0 })} />);

        const front = card().querySelector('.MuiPaper-root > div') as HTMLElement;
        expect(front).toHaveStyle({ opacity: '0.55' });
        expect(screen.getByText(/Next tier \(Bronze\)/)).toHaveTextContent('0/1');
    });

    it('falls back to a trophy when the icon is missing', () => {
        render(<AchievementCard achievement={achievement({ iconKey: 'missing' })} />);

        fireEvent.error(document.querySelector('img')!);

        expect(document.querySelector('img')).not.toBeInTheDocument();
        expect(screen.getAllByTestId('EmojiEventsIcon').length).toBeGreaterThan(0);
    });
});
