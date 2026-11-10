package com.reviewapp.android.util

import kotlin.math.max
import kotlin.math.roundToInt

/**
 * The size rules of the photos sent to camera recognition (the web's `utils/image.ts`): small enough to upload
 * quickly and keep the AI cheap, big enough to read the text on posters and covers. Pure, so it is unit-tested.
 */
object ImageDownscale {
    /** The longer side of an uploaded photo, in pixels. */
    const val MAX_DIMENSION = 1280

    /** JPEG quality of an uploaded photo, 0 to 100. */
    const val JPEG_QUALITY = 85

    /** A width and a height in pixels. */
    data class Size(val width: Int, val height: Int)

    /** The size that fits within [max] x [max], keeping the aspect ratio; never enlarges. */
    fun fitWithin(width: Int, height: Int, max: Int = MAX_DIMENSION): Size {
        if (width <= 0 || height <= 0) return Size(0, 0)
        val scale = minOf(1.0, max.toDouble() / max(width, height))
        return Size(max(1, (width * scale).roundToInt()), max(1, (height * scale).roundToInt()))
    }

    /**
     * The largest power of two to decode a [width] x [height] image with (BitmapFactory's inSampleSize) that still
     * leaves at least [max] pixels on the longer side, so a 12 MP photo is never fully loaded into memory.
     */
    fun sampleSize(width: Int, height: Int, max: Int = MAX_DIMENSION): Int {
        var sample = 1
        val longer = max(width, height)
        while (longer / (sample * 2) >= max) sample *= 2
        return sample
    }

    /** The clockwise rotation an EXIF orientation asks for (phones store portrait photos sideways plus this tag). */
    fun rotationDegrees(exifOrientation: Int): Int = when (exifOrientation) {
        6, 5 -> 90   // ORIENTATION_ROTATE_90, ORIENTATION_TRANSPOSE
        3, 4 -> 180  // ORIENTATION_ROTATE_180, ORIENTATION_FLIP_VERTICAL
        8, 7 -> 270  // ORIENTATION_ROTATE_270, ORIENTATION_TRANSVERSE
        else -> 0
    }
}
