package com.reviewapp.android.ui.offline

import androidx.compose.material3.SnackbarDuration
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.SnackbarResult
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.rememberUpdatedState
import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory
import com.reviewapp.android.data.sync.PushOutcome
import com.reviewapp.android.data.sync.SyncEngine
import com.reviewapp.android.di.appContainer
import com.reviewapp.android.util.pluralize
import kotlinx.coroutines.flow.Flow

/** What the root snackbar says after a sync sent changes made offline; [needsReview] adds a "Review" action. */
data class SyncSummaryMessage(val text: String, val needsReview: Boolean)

/**
 * "Synced 7 changes. You unlocked 1 achievement." (pure, unit-tested). Only for runs that sent changes which had
 * waited: a change made online is synced at once and needs no summary. Null when there is nothing to say.
 */
fun syncSummaryMessage(done: PushOutcome.Done): SyncSummaryMessage? {
    if (done.waited == 0 && done.waitedIssues == 0) return null
    val parts = buildList {
        if (done.waited > 0) add("Synced ${pluralize(done.applied, "change")}.")
        if (done.unlocked > 0) add("You unlocked ${pluralize(done.unlocked, "achievement")}.")
        overwrittenText(done.overwritten)?.let(::add)
        if (done.waitedIssues > 0) {
            add(if (done.waitedIssues == 1) "1 change needs your attention." else "${done.waitedIssues} changes need your attention.")
        }
    }
    return SyncSummaryMessage(parts.joinToString(" "), needsReview = done.waitedIssues > 0)
}

/** The latest intent wins for the profile: say so where a value changed elsewhere was replaced. */
private fun overwrittenText(fields: List<String>): String? {
    val names = fields.mapNotNull {
        when (it) {
            "bio" -> "bio"
            "profilePictureUrl" -> "profile picture"
            else -> null
        }
    }
    return when (names.size) {
        0 -> null
        1 -> "Your ${names[0]} replaced the one changed elsewhere."
        else -> "Your ${names.joinToString(" and ")} replaced the ones changed elsewhere."
    }
}

class SyncSummaryViewModel(syncEngine: SyncEngine) : ViewModel() {
    val summaries: Flow<PushOutcome.Done> = syncEngine.summaries

    companion object {
        val Factory: ViewModelProvider.Factory = viewModelFactory {
            initializer { SyncSummaryViewModel(appContainer().syncEngine) }
        }
    }
}

/** Shows the summary of each sync of offline changes on the root snackbar ("Review" opens the waiting changes). */
@Composable
fun SyncSummaryHost(
    snackbarHostState: SnackbarHostState,
    onReview: () -> Unit,
    viewModel: SyncSummaryViewModel = viewModel(factory = SyncSummaryViewModel.Factory),
) {
    val currentOnReview by rememberUpdatedState(onReview)
    LaunchedEffect(viewModel) {
        viewModel.summaries.collect { done ->
            val message = syncSummaryMessage(done) ?: return@collect
            val result = snackbarHostState.showSnackbar(
                message.text,
                actionLabel = if (message.needsReview) "Review" else null,
                duration = if (message.needsReview) SnackbarDuration.Long else SnackbarDuration.Short,
            )
            if (result == SnackbarResult.ActionPerformed) currentOnReview()
        }
    }
}
