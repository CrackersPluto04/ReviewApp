package com.reviewapp.android.util

import com.reviewapp.android.domain.model.Achievement
import com.reviewapp.android.domain.model.AchievementCategory

// Filtering and sorting of the Achievements tab, the same as the web's src/utils/achievements.ts.

/** The statuses overlap on purpose: a card with Bronze unlocked is both Unlocked and In progress. */
enum class AchievementStatus(val label: String) {
    Unlocked("Unlocked"), Locked("Locked"), InProgress("In progress");

    fun matches(achievement: Achievement): Boolean = when (this) {
        Unlocked -> achievement.unlockedCount > 0
        Locked -> achievement.unlockedCount == 0
        InProgress -> achievement.currentProgress > 0 && !achievement.isCompleted
    }
}

enum class AchievementSort(val label: String) {
    /** Cards with any unlocked tier first, otherwise in the API's (seed) order. */
    Default("Unlocked first"),
    Progress("Most progress"),
    Title("Title (A-Z)"),
}

/** Values are OR'd within a group and the groups AND'd; an empty group doesn't filter. */
data class AchievementFilter(
    val statuses: Set<AchievementStatus> = emptySet(),
    val categories: Set<AchievementCategory> = emptySet(),
    val sort: AchievementSort = AchievementSort.Default,
) {
    val isDefault: Boolean get() = statuses.isEmpty() && categories.isEmpty() && sort == AchievementSort.Default

    fun matches(achievement: Achievement): Boolean =
        (statuses.isEmpty() || statuses.any { it.matches(achievement) }) &&
            (categories.isEmpty() || categories.any { it in achievement.categories })

    fun apply(achievements: List<Achievement>): List<Achievement> {
        val filtered = achievements.filter(::matches)
        // sortedBy* are stable, so equal cards keep the API's order
        return when (sort) {
            AchievementSort.Default -> filtered.sortedBy { if (it.unlockedCount > 0) 0 else 1 }
            AchievementSort.Progress -> filtered.sortedByDescending { completionRatio(it) }
            AchievementSort.Title -> filtered.sortedWith(compareBy(String.CASE_INSENSITIVE_ORDER) { it.title })
        }
    }
}

/** Unlocked tiers plus the progress towards the next one, as a share of all tiers (0..1). */
fun completionRatio(achievement: Achievement): Double {
    val tierCount = achievement.tiers.size
    if (tierCount == 0) return 0.0
    val next = achievement.nextTier
    val partial = if (next == null || next.targetValue <= 0) 0.0
    else (achievement.currentProgress.toDouble() / next.targetValue).coerceAtMost(1.0)
    return (achievement.unlockedCount + partial) / tierCount
}

fun earnedTiers(achievements: List<Achievement>): Int = achievements.sumOf { it.unlockedCount }

fun totalTiers(achievements: List<Achievement>): Int = achievements.sumOf { it.tiers.size }
