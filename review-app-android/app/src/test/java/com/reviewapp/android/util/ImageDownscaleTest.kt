package com.reviewapp.android.util

import org.junit.Assert.assertEquals
import org.junit.Test

class ImageDownscaleTest {

    @Test
    fun fitWithin_keepsTheAspectRatio_andNeverEnlarges() {
        assertEquals(ImageDownscale.Size(1280, 960), ImageDownscale.fitWithin(4000, 3000))
        assertEquals(ImageDownscale.Size(960, 1280), ImageDownscale.fitWithin(3000, 4000))
        assertEquals(ImageDownscale.Size(640, 480), ImageDownscale.fitWithin(640, 480))
        assertEquals(ImageDownscale.Size(1280, 1), ImageDownscale.fitWithin(100_000, 10))
        assertEquals(ImageDownscale.Size(0, 0), ImageDownscale.fitWithin(0, 480))
        assertEquals(ImageDownscale.Size(500, 250), ImageDownscale.fitWithin(2000, 1000, max = 500))
    }

    @Test
    fun sampleSize_decodesNoBiggerThanNeeded_butNeverBelowTheTarget() {
        // 4032 x 3024 (12 MP): half is 2016 >= 1280, a quarter would be 1008 < 1280
        assertEquals(2, ImageDownscale.sampleSize(4032, 3024))
        // 8000 px: 4000, 2000 still >= 1280, 1000 not
        assertEquals(4, ImageDownscale.sampleSize(8000, 6000))
        assertEquals(1, ImageDownscale.sampleSize(1280, 720))
        assertEquals(1, ImageDownscale.sampleSize(2000, 1000))
        assertEquals(1, ImageDownscale.sampleSize(640, 480))
    }

    @Test
    fun rotationDegrees_followsTheExifOrientation() {
        assertEquals(0, ImageDownscale.rotationDegrees(1))
        assertEquals(0, ImageDownscale.rotationDegrees(0))
        assertEquals(90, ImageDownscale.rotationDegrees(6))
        assertEquals(180, ImageDownscale.rotationDegrees(3))
        assertEquals(270, ImageDownscale.rotationDegrees(8))
    }
}
