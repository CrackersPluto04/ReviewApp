import { afterEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor } from '@testing-library/preact';
import { CameraCapture, cameraErrorMessage } from '../../src/components/CameraCapture';
import { renderWithApp } from '../support/render';
import { removeCamera, resetVisibility, setVisibility, stubCamera } from '../support/browser';

vi.mock('../../src/utils/image', () => ({
    captureFrame: vi.fn(async () => new Blob(['frame'], { type: 'image/jpeg' })),
}));

function renderCamera() {
    const onCapture = vi.fn();
    const onUnavailable = vi.fn();
    const result = renderWithApp(<CameraCapture onCapture={onCapture} onUnavailable={onUnavailable} />);
    return { ...result, onCapture, onUnavailable };
}

describe('CameraCapture', () => {
    afterEach(() => {
        removeCamera();
        resetVisibility();
    });

    it('shows the back camera and takes a photo, then turns the camera off', async () => {
        const camera = stubCamera();
        const { user, onCapture } = renderCamera();

        await waitFor(() => expect(screen.getByRole('button', { name: 'Take photo' })).toBeEnabled());
        expect(camera.getUserMedia).toHaveBeenCalledWith(expect.objectContaining({
            audio: false,
            video: expect.objectContaining({ facingMode: { ideal: 'environment' } }),
        }));
        expect(screen.getByLabelText('Camera preview')).toBeInTheDocument();

        await user.click(screen.getByRole('button', { name: 'Take photo' }));

        await waitFor(() => expect(onCapture).toHaveBeenCalledWith(expect.any(Blob)));
        expect(camera.track.stop).toHaveBeenCalled();
    });

    it('turns the camera off when it goes away', async () => {
        const camera = stubCamera();
        const { unmount } = renderCamera();
        await waitFor(() => expect(screen.getByRole('button', { name: 'Take photo' })).toBeEnabled());

        unmount();

        expect(camera.track.stop).toHaveBeenCalled();
    });

    it('offers switching between several cameras', async () => {
        const camera = stubCamera({ cameras: 2 });
        const { user } = renderCamera();

        await user.click(await screen.findByRole('button', { name: 'Switch camera' }));

        await waitFor(() => expect(camera.getUserMedia).toHaveBeenLastCalledWith(expect.objectContaining({
            video: expect.objectContaining({ facingMode: { ideal: 'user' } }),
        })));
        expect(camera.track.stop).toHaveBeenCalled();
    });

    it('has no switch with one camera', async () => {
        stubCamera();
        renderCamera();

        await waitFor(() => expect(screen.getByRole('button', { name: 'Take photo' })).toBeEnabled());
        expect(screen.queryByRole('button', { name: 'Switch camera' })).not.toBeInTheDocument();
    });

    it('turns the camera off in a hidden tab, and back on when asked', async () => {
        const camera = stubCamera();
        const { user } = renderCamera();
        await waitFor(() => expect(screen.getByRole('button', { name: 'Take photo' })).toBeEnabled());

        setVisibility('hidden');

        expect(await screen.findByText(/turned off while the page was in the background/)).toBeInTheDocument();
        await waitFor(() => expect(camera.track.stop).toHaveBeenCalled());

        setVisibility('visible');
        await user.click(screen.getByRole('button', { name: 'Turn the camera back on' }));
        await waitFor(() => expect(camera.getUserMedia).toHaveBeenCalledTimes(2));
    });

    it('reports a denied camera', async () => {
        stubCamera({ error: 'NotAllowedError' });
        const { onUnavailable } = renderCamera();

        await waitFor(() => expect(onUnavailable).toHaveBeenCalledWith(expect.stringMatching(/Camera access was denied/)));
    });

    it('reports a browser without camera support', async () => {
        const { onUnavailable } = renderCamera();

        await waitFor(() => expect(onUnavailable).toHaveBeenCalledWith(expect.stringMatching(/not available in this browser/)));
    });

    it.each([
        ['NotAllowedError', /denied/],
        ['NotFoundError', /No camera was found/],
        ['NotReadableError', /in use by another app/],
        ['TypeError', /could not be started/],
    ])('explains %s', (name, message) => {
        expect(cameraErrorMessage(new DOMException('x', name))).toMatch(message);
    });
});
