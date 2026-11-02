// Photos are made smaller in the browser before the upload: quicker, cheaper for the AI, and still sharp enough
// to read the text on posters and covers. The backend enforces its own limits regardless.
export const MAX_PHOTO_DIMENSION = 1280;
export const PHOTO_QUALITY = 0.85;

// The size that fits within max x max, keeping the aspect ratio; never enlarges
export function fitWithin(width: number, height: number, max: number = MAX_PHOTO_DIMENSION): { width: number, height: number } {
    if (width <= 0 || height <= 0)
        return { width: 0, height: 0 };

    const scale = Math.min(1, max / Math.max(width, height));
    return { width: Math.max(1, Math.round(width * scale)), height: Math.max(1, Math.round(height * scale)) };
}

// A picked file (any format the browser can decode, phone photos rotated by their EXIF orientation) as a smaller JPEG
export async function downscaleToJpeg(file: Blob, max: number = MAX_PHOTO_DIMENSION): Promise<Blob> {
    const bitmap = await createImageBitmap(file, { imageOrientation: 'from-image' });
    try {
        return await drawToJpeg(bitmap, bitmap.width, bitmap.height, max);
    } finally {
        bitmap.close();
    }
}

// The current frame of the webcam as a JPEG
export function captureFrame(video: HTMLVideoElement, max: number = MAX_PHOTO_DIMENSION): Promise<Blob> {
    return drawToJpeg(video, video.videoWidth, video.videoHeight, max);
}

function drawToJpeg(source: CanvasImageSource, width: number, height: number, max: number): Promise<Blob> {
    const size = fitWithin(width, height, max);
    const canvas = document.createElement('canvas');
    canvas.width = size.width;
    canvas.height = size.height;

    const context = canvas.getContext('2d');
    if (!context)
        return Promise.reject(new Error('Images cannot be processed in this browser.'));
    context.drawImage(source, 0, 0, size.width, size.height);

    return new Promise((resolve, reject) => canvas.toBlob(
        blob => blob ? resolve(blob) : reject(new Error('The photo could not be prepared.')),
        'image/jpeg', PHOTO_QUALITY));
}
