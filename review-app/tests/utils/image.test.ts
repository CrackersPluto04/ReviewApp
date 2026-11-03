import { describe, expect, it, vi } from 'vitest';
import { captureFrame, downscaleToJpeg, fitWithin, MAX_PHOTO_DIMENSION, PHOTO_QUALITY } from '../../src/utils/image';

// jsdom has no canvas drawing: the 2D context and toBlob are stand-ins recording what the code asked for
function stubCanvas(blob: Blob | null = new Blob(['jpeg'], { type: 'image/jpeg' })) {
    const drawImage = vi.fn();
    const sizes: { width: number, height: number }[] = [];
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockImplementation(function (this: HTMLCanvasElement) {
        sizes.push({ width: this.width, height: this.height });
        return { drawImage } as any;
    });
    const toBlob = vi.spyOn(HTMLCanvasElement.prototype, 'toBlob').mockImplementation((callback: BlobCallback) => callback(blob));
    return { drawImage, sizes, toBlob };
}

describe('image utils', () => {
    describe('fitWithin', () => {
        it.each([
            [4000, 3000, { width: 1280, height: 960 }],
            [3000, 4000, { width: 960, height: 1280 }],
            [1280, 1280, { width: 1280, height: 1280 }],
            // Never enlarged
            [640, 480, { width: 640, height: 480 }],
            // Extreme panorama keeps at least a pixel
            [100000, 10, { width: 1280, height: 1 }],
            [0, 480, { width: 0, height: 0 }],
        ])('%i x %i', (width, height, expected) => {
            expect(fitWithin(width, height)).toEqual(expected);
        });

        it('uses the given maximum', () => {
            expect(fitWithin(2000, 1000, 500)).toEqual({ width: 500, height: 250 });
        });
    });

    it('downscales a picked photo upright to a JPEG', async () => {
        const close = vi.fn();
        const createImageBitmap = vi.fn(async () => ({ width: 4032, height: 3024, close }));
        vi.stubGlobal('createImageBitmap', createImageBitmap);
        const canvas = stubCanvas();
        const file = new Blob(['heic-or-jpeg']);

        const result = await downscaleToJpeg(file);

        expect(result.type).toBe('image/jpeg');
        // The EXIF orientation is applied, so phone photos aren't sideways
        expect(createImageBitmap).toHaveBeenCalledWith(file, { imageOrientation: 'from-image' });
        expect(canvas.sizes[0]).toEqual({ width: MAX_PHOTO_DIMENSION, height: 960 });
        expect(canvas.drawImage).toHaveBeenCalledWith(expect.anything(), 0, 0, MAX_PHOTO_DIMENSION, 960);
        expect(canvas.toBlob).toHaveBeenCalledWith(expect.any(Function), 'image/jpeg', PHOTO_QUALITY);
        expect(close).toHaveBeenCalled();
    });

    it('rejects a file the browser can\'t decode', async () => {
        vi.stubGlobal('createImageBitmap', vi.fn(async () => { throw new DOMException('bad', 'InvalidStateError'); }));

        await expect(downscaleToJpeg(new Blob(['not an image']))).rejects.toThrow();
    });

    it('captures the current webcam frame', async () => {
        const canvas = stubCanvas();
        const video = document.createElement('video');
        Object.defineProperty(video, 'videoWidth', { value: 1920 });
        Object.defineProperty(video, 'videoHeight', { value: 1080 });

        await captureFrame(video);

        expect(canvas.sizes[0]).toEqual({ width: 1280, height: 720 });
        expect(canvas.drawImage).toHaveBeenCalledWith(video, 0, 0, 1280, 720);
    });

    it('fails clearly when the browser can\'t encode', async () => {
        stubCanvas(null);
        const video = document.createElement('video');
        Object.defineProperty(video, 'videoWidth', { value: 10 });
        Object.defineProperty(video, 'videoHeight', { value: 10 });

        await expect(captureFrame(video)).rejects.toThrow('The photo could not be prepared.');
    });
});
