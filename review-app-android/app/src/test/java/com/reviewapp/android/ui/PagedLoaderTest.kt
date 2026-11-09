package com.reviewapp.android.ui

import com.reviewapp.android.data.remote.ApiResult
import com.reviewapp.android.domain.model.Page
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.advanceUntilIdle
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

// Lists offline: they stop at what the device has instead of failing, and say how old it is
@OptIn(ExperimentalCoroutinesApi::class)
class PagedLoaderTest {

    private val offline = ApiResult.Error("Could not reach the server.", isNetwork = true)

    private fun TestScope.loader(answers: MutableList<ApiResult<Page<Int>>>) =
        PagedLoader(this, itemKey = { it }) { _ -> answers.removeAt(0) }

    @Test
    fun aLaterPageOffline_EndsTheListWithoutAnError() = runTest {
        val answers = mutableListOf<ApiResult<Page<Int>>>(ApiResult.Success(Page(listOf(1, 2), 1, 3, 6)), offline)
        val loader = loader(answers)

        loader.reload(); advanceUntilIdle()
        loader.loadMore(); advanceUntilIdle()

        val state = loader.state.value
        assertEquals(listOf(1, 2), state.items)
        assertTrue(state.offlineEnd)
        assertTrue(state.endReached)
        assertNull(state.error)
    }

    @Test
    fun theFirstPageOffline_IsAnErrorThatSaysWhy() = runTest {
        val loader = loader(mutableListOf(offline, ApiResult.Error("Not found.", code = 404)))

        loader.reload(); advanceUntilIdle()
        assertEquals(PagedLoader.OFFLINE_NOT_SAVED, loader.state.value.error)
        assertFalse(loader.state.value.offlineEnd)

        // Other errors keep the API's message
        loader.reload(); advanceUntilIdle()
        assertEquals("Not found.", loader.state.value.error)
    }

    @Test
    fun retry_ContinuesAfterAnOfflineEnd() = runTest {
        val answers = mutableListOf<ApiResult<Page<Int>>>(
            ApiResult.Success(Page(listOf(1), 1, 2, 2)),
            offline,
            ApiResult.Success(Page(listOf(2), 2, 2, 2)),
        )
        val loader = loader(answers)

        loader.reload(); advanceUntilIdle()
        loader.loadMore(); advanceUntilIdle()
        loader.retry(); advanceUntilIdle()

        assertEquals(listOf(1, 2), loader.state.value.items)
        assertFalse(loader.state.value.offlineEnd)
    }

    @Test
    fun theOldestOfflineCopy_DecidesHowOldTheListIs() = runTest {
        val answers = mutableListOf<ApiResult<Page<Int>>>(
            ApiResult.Success(Page(listOf(1), 1, 3, 3), cachedAt = 500),
            ApiResult.Success(Page(listOf(2), 2, 3, 3), cachedAt = 300),
            ApiResult.Success(Page(listOf(3), 3, 3, 3)),
        )
        val loader = loader(answers)

        loader.reload(); advanceUntilIdle()
        assertEquals(500L, loader.state.value.updatedAt)
        loader.loadMore(); advanceUntilIdle()
        assertEquals(300L, loader.state.value.updatedAt)
        loader.loadMore(); advanceUntilIdle()
        assertEquals(300L, loader.state.value.updatedAt)
    }
}
