package com.reviewapp.android.util

import com.reviewapp.android.domain.model.Achievement
import com.reviewapp.android.domain.model.AchievementCategory
import com.reviewapp.android.domain.model.AchievementTier
import com.reviewapp.android.domain.model.AchievementTierInfo
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class AchievementsTest {

    /** Targets 1 / 10 / 50 like most seeded cards. */
    private fun achievement(
        code: String,
        progress: Int,
        vararg categories: AchievementCategory,
        targets: List<Int> = listOf(1, 10, 50),
        title: String = code,
    ) = Achievement(
        groupCode = code,
        title = title,
        iconKey = code.lowercase(),
        categories = categories.toSet(),
        currentProgress = progress,
        tiers = AchievementTier.entries.zip(targets) { tier, target ->
            AchievementTierInfo(tier, "Reach $target.", target, progress >= target)
        },
    )

    private val locked = achievement("LOCKED", 0, AchievementCategory.Movie, AchievementCategory.Review)
    // Progress but nothing unlocked yet (targets start at 10)
    private val started = achievement("STARTED", 4, AchievementCategory.Reply, targets = listOf(10, 50, 100))
    private val bronze = achievement("BRONZE", 3, AchievementCategory.Series, AchievementCategory.Review)
    private val done = achievement("DONE", 60, AchievementCategory.Music, AchievementCategory.Review)
    private val all = listOf(locked, started, bronze, done)

    @Test
    fun tiers_nextAndCompleted() {
        assertEquals(AchievementTier.Silver, bronze.nextTier?.tier)
        assertEquals(1, bronze.unlockedCount)
        assertFalse(bronze.isCompleted)
        assertNull(done.nextTier)
        assertTrue(done.isCompleted)
        assertEquals(4, earnedTiers(all))
        assertEquals(12, totalTiers(all))
    }

    @Test
    fun statuses_overlapLikeTheWeb() {
        fun codes(status: AchievementStatus) = all.filter(status::matches).map { it.groupCode }

        assertEquals(listOf("BRONZE", "DONE"), codes(AchievementStatus.Unlocked))
        assertEquals(listOf("LOCKED", "STARTED"), codes(AchievementStatus.Locked))
        // Completed cards aren't in progress, started but still locked ones are
        assertEquals(listOf("STARTED", "BRONZE"), codes(AchievementStatus.InProgress))
    }

    @Test
    fun filter_orWithinGroup_andAcrossGroups() {
        fun codes(filter: AchievementFilter) = filter.apply(all).map { it.groupCode }

        assertEquals(all.size, AchievementFilter().apply(all).size)
        assertEquals(
            listOf("BRONZE", "DONE", "LOCKED", "STARTED"),
            codes(AchievementFilter(statuses = setOf(AchievementStatus.Unlocked, AchievementStatus.Locked))),
        )
        assertEquals(
            listOf("LOCKED", "STARTED"),
            codes(AchievementFilter(categories = setOf(AchievementCategory.Movie, AchievementCategory.Reply))),
        )
        assertEquals(
            listOf("BRONZE", "DONE"),
            codes(AchievementFilter(statuses = setOf(AchievementStatus.Unlocked), categories = setOf(AchievementCategory.Review))),
        )
        assertTrue(AchievementFilter().isDefault)
        assertFalse(AchievementFilter(sort = AchievementSort.Title).isDefault)
    }

    @Test
    fun sorts() {
        fun codes(sort: AchievementSort) = AchievementFilter(sort = sort).apply(all).map { it.groupCode }

        // Stable: unlocked ones first, each group in the API's order
        assertEquals(listOf("BRONZE", "DONE", "LOCKED", "STARTED"), codes(AchievementSort.Default))
        // DONE 3/3, BRONZE (1 + 3/10) / 3, STARTED (4/10) / 3, LOCKED 0
        assertEquals(listOf("DONE", "BRONZE", "STARTED", "LOCKED"), codes(AchievementSort.Progress))
        val titled = listOf(achievement("A", 0, title = "curator"), achievement("B", 0, title = "Binge Watcher"))
        assertEquals(listOf("B", "A"), AchievementFilter(sort = AchievementSort.Title).apply(titled).map { it.groupCode })
    }

    @Test
    fun completionRatio_capsPartialProgress() {
        assertEquals(1.0, completionRatio(done), 1e-9)
        assertEquals((1 + 0.3) / 3, completionRatio(bronze), 1e-9)
        assertEquals(0.0, completionRatio(locked), 1e-9)
    }
}
