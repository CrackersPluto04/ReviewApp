import { describe, expect, it } from 'vitest';
import { screen } from '@testing-library/preact';
import { PageContainer } from '../../src/components/PageContainer';
import { authUser } from '../support/builders';
import { renderWithApp } from '../support/render';

describe('PageContainer', () => {
    it('wraps the page in the header, the verification banner and the footer', async () => {
        renderWithApp(<PageContainer mode="light" toggleTheme={() => {}}><p>The page</p></PageContainer>, {
            user: authUser({ isEmailConfirmed: false }),
        });

        expect(screen.getByRole('banner')).toHaveTextContent('THE Review App');
        expect(screen.getByRole('main')).toHaveTextContent('The page');
        expect(await screen.findByText(/Please verify your email address/)).toBeInTheDocument();
        expect(screen.getByText(/All rights reserved/)).toBeInTheDocument();
    });
});
