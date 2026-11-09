package com.reviewapp.android.data.local.offline

import org.junit.Assert.assertEquals
import org.junit.Test

class PosterUrlsTest {
    @Test
    fun tmdbPosters_UseTheSmallWidth() {
        assertEquals("https://image.tmdb.org/t/p/w185/abc.jpg", PosterUrls.small("https://image.tmdb.org/t/p/w500/abc.jpg"))
        assertEquals("https://image.tmdb.org/t/p/w185/abc.jpg", PosterUrls.small("https://image.tmdb.org/t/p/original/abc.jpg"))
    }

    @Test
    fun otherImages_StayAsTheyAre() {
        assertEquals("https://i.scdn.co/image/ab67616d0000b273", PosterUrls.small("https://i.scdn.co/image/ab67616d0000b273"))
    }
}
