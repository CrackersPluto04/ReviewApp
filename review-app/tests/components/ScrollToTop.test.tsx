import { describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/preact';
import { MemoryRouter, Route, Routes, useNavigate } from 'react-router-dom';
import { ScrollToTop } from '../../src/components/ScrollToTop';
import { setupUser } from '../support/render';

describe('ScrollToTop', () => {
    function Nav() {
        const navigate = useNavigate();
        return <>
            <button onClick={() => navigate('/b')}>to b</button>
            <button onClick={() => navigate('/b?page=2')}>to b page 2</button>
        </>;
    }

    it('scrolls to the top when the path changes, not on query changes', async () => {
        const scrollTo = vi.mocked(window.scrollTo);
        scrollTo.mockClear();
        const user = setupUser();
        render(<MemoryRouter initialEntries={['/a']}><ScrollToTop /><Routes><Route path="*" element={<Nav />} /></Routes></MemoryRouter>);
        expect(scrollTo).toHaveBeenCalledTimes(1);

        // Preact runs effects after the next paint
        await user.click(screen.getByRole('button', { name: 'to b' }));
        await waitFor(() => expect(scrollTo).toHaveBeenCalledTimes(2));
        expect(scrollTo).toHaveBeenLastCalledWith(0, 0);

        await user.click(screen.getByRole('button', { name: 'to b page 2' }));
        await new Promise(resolve => setTimeout(resolve, 100));
        expect(scrollTo).toHaveBeenCalledTimes(2);
    });
});
