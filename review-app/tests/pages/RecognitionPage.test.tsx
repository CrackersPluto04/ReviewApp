import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor, within } from '@testing-library/preact';
import { HttpResponse } from 'msw';
import { RecognitionPage } from '../../src/pages/RecognitionPage';
import { server } from '../support/server';
import { apiError, capture, deferred, ok, paymentRequired } from '../support/api';
import { authUser, premiumAuthUser, recognitionAllowance, recognitionResponse, recognizedMedia } from '../support/builders';
import { currentLocation, renderWithApp } from '../support/render';
import { serveStats } from '../support/handlers';
import { removeCamera, stubCamera, stubObjectUrls } from '../support/browser';

// Canvas work can't run in jsdom; the image helpers are tested on their own
vi.mock('../../src/utils/image', () => ({
    downscaleToJpeg: vi.fn(async () => new Blob(['downscaled'], { type: 'image/jpeg' })),
    captureFrame: vi.fn(async () => new Blob(['frame'], { type: 'image/jpeg' })),
}));

function serveAllowance(allowance = recognitionAllowance()) {
    const endpoint = capture('get', 'Recognition/allowance', ok(allowance));
    server.use(endpoint.handler);
    return endpoint;
}

function serveRecognition(response: Response | (() => Response | Promise<Response>) = ok(recognitionResponse())) {
    const endpoint = capture('post', 'Recognition', response);
    server.use(endpoint.handler);
    return endpoint;
}

async function renderPage(user = premiumAuthUser()) {
    const result = renderWithApp(<RecognitionPage />, { user, route: '/recognize' });
    return result;
}

// Picks a file through the hidden input, like the "Choose a photo" button does
async function choosePhoto(user: Awaited<ReturnType<typeof renderPage>>['user']) {
    await screen.findByRole('button', { name: 'Choose a photo' });
    await user.upload(screen.getByTestId('photo-input'), new File(['raw'], 'figure.jpg', { type: 'image/jpeg' }));
    await screen.findByRole('img', { name: 'Your photo' });
}

describe('RecognitionPage', () => {
    beforeEach(() => {
        stubObjectUrls();
        serveStats();
    });

    afterEach(() => removeCamera());

    it('asks a visitor to log in', async () => {
        renderWithApp(<RecognitionPage />, { user: null });

        expect(await screen.findByText('Log in to use camera recognition.')).toBeInTheDocument();
    });

    it('shows a Free user the upsell instead of the camera', async () => {
        serveAllowance(recognitionAllowance({ premium: false, limit: null, remaining: null, resetsAt: null }));
        renderPage(authUser());

        expect(await screen.findByText('Camera recognition is a Premium feature')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'See Premium' })).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Choose a photo' })).not.toBeInTheDocument();
    });

    it('says when the feature is switched off', async () => {
        serveAllowance(recognitionAllowance({ enabled: false }));
        renderPage();

        expect(await screen.findByText(/temporarily unavailable/)).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Choose a photo' })).not.toBeInTheDocument();
    });

    it('can retry loading the allowance', async () => {
        server.use(capture('get', 'Recognition/allowance', apiError(500, 'Could not load.')).handler);
        const { user } = await renderPage();

        expect(await screen.findByText('Could not load.')).toBeInTheDocument();
        serveAllowance();
        await user.click(screen.getByRole('button', { name: 'Retry' }));

        expect(await screen.findByRole('button', { name: 'Choose a photo' })).toBeInTheDocument();
    });

    it('recognises a chosen photo and pages through the suggestions', async () => {
        serveAllowance();
        const recognition = serveRecognition();
        const more = capture('get', 'Recognition/rec-1/items', ok({
            items: [recognizedMedia('people', { title: 'Suggestion 11' }), recognizedMedia('people', { title: 'Suggestion 12' })],
            total: 12, hasMore: false, nextOffset: 12,
        }));
        server.use(more.handler);
        const { user } = await renderPage();

        expect(await screen.findByText('14 of 20 left today')).toBeInTheDocument();
        await choosePhoto(user);
        await user.click(screen.getByRole('button', { name: 'Use photo' }));

        expect(await screen.findByText('We saw an Iron Man statue.')).toBeInTheDocument();
        expect(screen.getAllByRole('img', { name: /^Suggestion/ })).toHaveLength(10);
        expect(screen.getByRole('heading', { name: 'Direct match' })).toBeInTheDocument();
        expect(screen.getByRole('heading', { name: 'Same universe' })).toBeInTheDocument();
        expect(screen.getByText('13 of 20 left today')).toBeInTheDocument();
        // The downscaled photo went up as multipart
        expect(recognition.last.headers.get('content-type')).toMatch(/^multipart\/form-data/);
        expect(recognition.last.body).toContain('downscaled');

        await user.click(screen.getByRole('button', { name: 'Show more' }));

        expect(await screen.findByRole('heading', { name: 'Related people' })).toBeInTheDocument();
        expect(screen.getAllByRole('img', { name: /^Suggestion/ })).toHaveLength(12);
        expect(new URL(more.last.url).searchParams.get('offset')).toBe('10');
        expect(screen.getByText("That's everything we found.")).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Show more' })).not.toBeInTheDocument();
        // "Show more" is no new recognition
        expect(recognition.calls).toHaveLength(1);
    });

    it('opens the media page from a suggestion', async () => {
        serveAllowance();
        serveRecognition(ok(recognitionResponse(1, {
            items: [recognizedMedia('direct', { title: 'Iron Man', externalApiID: '1726', mediaType: 0 })], hasMore: false,
        })));
        const { user } = await renderPage();
        await choosePhoto(user);

        await user.click(screen.getByRole('button', { name: 'Use photo' }));
        await user.click(await screen.findByRole('img', { name: 'Iron Man' }));

        expect(currentLocation()).toBe('/media/0/1726');
    });

    it('keeps the list when "Show more" fails, and offers a retry', async () => {
        serveAllowance();
        serveRecognition();
        server.use(capture('get', 'Recognition/rec-1/items', apiError(500, 'Could not load more.')).handler);
        const { user } = await renderPage();
        await choosePhoto(user);
        await user.click(screen.getByRole('button', { name: 'Use photo' }));

        await user.click(await screen.findByRole('button', { name: 'Show more' }));

        expect(await screen.findByText('Could not load more.')).toBeInTheDocument();
        expect(screen.getAllByRole('img', { name: /^Suggestion/ })).toHaveLength(10);
        expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
    });

    it('says when the suggestions have expired', async () => {
        serveAllowance();
        serveRecognition();
        server.use(capture('get', 'Recognition/rec-1/items', HttpResponse.json(
            { error: 'This recognition has expired.', code: 'not_found' }, { status: 404 })).handler);
        const { user } = await renderPage();
        await choosePhoto(user);
        await user.click(screen.getByRole('button', { name: 'Use photo' }));

        await user.click(await screen.findByRole('button', { name: 'Show more' }));

        expect(await screen.findByText(/These suggestions have expired/)).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument();
    });

    it('explains a photo with nothing to suggest, with tips', async () => {
        serveAllowance();
        serveRecognition(ok(recognitionResponse(0, { outcome: 'not_media_related', summary: 'We saw a coffee mug.' })));
        const { user } = await renderPage();
        await choosePhoto(user);

        await user.click(screen.getByRole('button', { name: 'Use photo' }));

        expect(await screen.findByText('We saw a coffee mug.')).toBeInTheDocument();
        expect(screen.getByText(/doesn't look related to movies, series or music/)).toBeInTheDocument();
        expect(screen.getByText(/Photograph one object at a time/)).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Show more' })).not.toBeInTheDocument();
    });

    it('warns that a list may be incomplete', async () => {
        serveAllowance();
        serveRecognition(ok(recognitionResponse(3, { incomplete: true })));
        const { user } = await renderPage();
        await choosePhoto(user);

        await user.click(screen.getByRole('button', { name: 'Use photo' }));

        expect(await screen.findByText(/the list may be incomplete/)).toBeInTheDocument();
    });

    it('can cancel a running recognition and discard its result', async () => {
        serveAllowance();
        const response = deferred(() => ok(recognitionResponse()));
        serveRecognition(() => response.promise);
        const { user } = await renderPage();
        await choosePhoto(user);

        await user.click(screen.getByRole('button', { name: 'Use photo' }));
        expect(await screen.findByText('Recognising your photo...')).toBeInTheDocument();
        await user.click(screen.getByRole('button', { name: 'Cancel' }));

        expect(await screen.findByRole('button', { name: 'Use photo' })).toBeInTheDocument();
        response.release();
        await new Promise(resolve => setTimeout(resolve, 50));
        expect(screen.queryByText('We saw an Iron Man statue.')).not.toBeInTheDocument();
    });

    it('keeps the photo after a failure, so it can be sent again', async () => {
        serveAllowance();
        serveRecognition(HttpResponse.json({ error: 'Camera recognition is temporarily unavailable.', code: 'recognition_unavailable' }, { status: 503 }));
        const { user } = await renderPage();
        await choosePhoto(user);

        await user.click(screen.getByRole('button', { name: 'Use photo' }));

        expect(await screen.findByText('Camera recognition is temporarily unavailable.')).toBeInTheDocument();
        expect(screen.getByRole('img', { name: 'Your photo' })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Use photo' })).toBeEnabled();
    });

    it('stops at the daily limit', async () => {
        serveAllowance();
        serveRecognition(HttpResponse.json(
            { error: 'You have used all 20 recognitions for today.', code: 'daily_limit', resetsAt: '2026-11-03T00:00:00Z' }, { status: 429 }));
        const { user } = await renderPage();
        await choosePhoto(user);

        await user.click(screen.getByRole('button', { name: 'Use photo' }));

        expect(await screen.findByText('You have used all 20 recognitions for today.')).toBeInTheDocument();
        expect(screen.getByText('0 of 20 left today')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Use photo' })).toBeDisabled();
    });

    it('shows the limit before a photo is taken when nothing is left', async () => {
        serveAllowance(recognitionAllowance({ remaining: 0 }));
        renderPage();

        expect(await screen.findByText(/used all your recognitions for today/)).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Choose a photo' })).not.toBeInTheDocument();
    });

    it('turns into the upsell when the subscription ended meanwhile', async () => {
        serveAllowance();
        serveRecognition(paymentRequired());
        const { user } = await renderPage();
        await choosePhoto(user);

        await user.click(screen.getByRole('button', { name: 'Use photo' }));

        expect(await screen.findByRole('dialog', { name: 'This is a Premium feature' })).toBeInTheDocument();
        expect(screen.getByText('Camera recognition is a Premium feature')).toBeInTheDocument();
    });

    it('takes a photo with the camera, and can retake it', async () => {
        serveAllowance();
        const camera = stubCamera();
        const { user } = await renderPage();

        await user.click(await screen.findByRole('button', { name: 'Take a photo' }));
        await waitFor(() => expect(screen.getByRole('button', { name: 'Take photo' })).toBeEnabled());
        await user.click(screen.getByRole('button', { name: 'Take photo' }));

        expect(await screen.findByRole('img', { name: 'Your photo' })).toBeInTheDocument();
        await waitFor(() => expect(camera.track.stop).toHaveBeenCalled());

        await user.click(screen.getByRole('button', { name: 'Retake' }));
        expect(await screen.findByRole('button', { name: 'Take photo' })).toBeInTheDocument();
        await waitFor(() => expect(camera.getUserMedia).toHaveBeenCalledTimes(2));
    });

    it('falls back to choosing a photo when the camera is denied', async () => {
        serveAllowance();
        stubCamera({ error: 'NotAllowedError' });
        const { user } = await renderPage();

        await user.click(await screen.findByRole('button', { name: 'Take a photo' }));

        expect(await screen.findByText(/Camera access was denied/)).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Take a photo' })).not.toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Choose a photo' })).toBeInTheDocument();
    });

    it('tells about the AI service until the notice is dismissed', async () => {
        serveAllowance();
        const { user, unmount } = await renderPage();

        const notice = await screen.findByText(/sent to an external AI service/);
        await user.click(within(notice.closest('[role="alert"]') as HTMLElement).getByRole('button', { name: 'Close' }));

        expect(screen.queryByText(/sent to an external AI service/)).not.toBeInTheDocument();
        unmount();
        serveAllowance();
        renderPage();
        await screen.findByRole('button', { name: 'Choose a photo' });
        expect(screen.queryByText(/sent to an external AI service/)).not.toBeInTheDocument();
    });

    it('goes back to the start for another photo', async () => {
        serveAllowance();
        serveRecognition();
        const { user } = await renderPage();
        await choosePhoto(user);
        await user.click(screen.getByRole('button', { name: 'Use photo' }));

        await user.click(await screen.findByRole('button', { name: 'Try another photo' }));

        expect(await screen.findByRole('button', { name: 'Choose a photo' })).toBeInTheDocument();
        expect(screen.queryByText('We saw an Iron Man statue.')).not.toBeInTheDocument();
    });
});
