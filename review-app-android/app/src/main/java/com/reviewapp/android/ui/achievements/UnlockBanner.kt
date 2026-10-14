package com.reviewapp.android.ui.achievements

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.slideInVertically
import androidx.compose.animation.slideOutVertically
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.EmojiEvents
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewModelScope
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory
import com.reviewapp.android.data.AuthState
import com.reviewapp.android.data.SessionManager
import com.reviewapp.android.data.repository.AchievementRepository
import com.reviewapp.android.di.appContainer
import com.reviewapp.android.domain.model.UnlockedAchievement
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

/** Activity-scoped queue of unlocked tiers, shown one banner at a time (the web's achievement toasts). */
class UnlockBannerViewModel(
    achievementRepository: AchievementRepository,
    private val sessionManager: SessionManager,
) : ViewModel() {

    private val queue = ArrayDeque<UnlockedAchievement>()
    private val _current = MutableStateFlow<UnlockedAchievement?>(null)
    val current: StateFlow<UnlockedAchievement?> = _current.asStateFlow()

    private var nextJob: Job? = null

    init {
        viewModelScope.launch {
            achievementRepository.unlocks.collect { unlocked ->
                queue.addLast(unlocked)
                if (_current.value == null && nextJob?.isActive != true) showNext()
            }
        }
        viewModelScope.launch {
            // Someone else's unlocks must not show up after a logout
            sessionManager.state.collect { if (it is AuthState.LoggedOut) clear() }
        }
    }

    val currentUsername: String? get() = sessionManager.currentUser?.username

    /** Hides the banner; the next one comes after the exit animation, like the web's toast queue. */
    fun dismiss() {
        if (_current.value == null) return
        _current.value = null
        nextJob = viewModelScope.launch {
            delay(EXIT_DELAY_MS)
            showNext()
        }
    }

    private fun showNext() {
        _current.value = queue.removeFirstOrNull()
    }

    private fun clear() {
        queue.clear()
        nextJob?.cancel()
        _current.value = null
    }

    companion object {
        const val SHOW_MS = 5_000L
        private const val EXIT_DELAY_MS = 400L

        val Factory: ViewModelProvider.Factory = viewModelFactory {
            initializer {
                val c = appContainer()
                UnlockBannerViewModel(c.achievementRepository, c.sessionManager)
            }
        }
    }
}

/**
 * Slides an "Achievement unlocked!" banner in at the top for each unlocked tier. Tapping it opens the
 * current user's Achievements tab through [onOpenAchievements].
 */
@Composable
fun UnlockBannerHost(
    onOpenAchievements: (username: String) -> Unit,
    modifier: Modifier = Modifier,
    viewModel: UnlockBannerViewModel = viewModel(factory = UnlockBannerViewModel.Factory),
) {
    val current by viewModel.current.collectAsStateWithLifecycle()
    // The banner keeps its content while it animates out
    var shown by remember { mutableStateOf<UnlockedAchievement?>(null) }
    if (current != null) shown = current

    LaunchedEffect(current) {
        if (current != null) {
            delay(UnlockBannerViewModel.SHOW_MS)
            viewModel.dismiss()
        }
    }

    AnimatedVisibility(
        visible = current != null,
        enter = slideInVertically { -it } + fadeIn(),
        exit = slideOutVertically { -it } + fadeOut(),
        modifier = modifier.statusBarsPadding().padding(horizontal = 16.dp, vertical = 8.dp),
    ) {
        val unlocked = shown ?: return@AnimatedVisibility
        UnlockBanner(
            unlocked = unlocked,
            onClick = {
                viewModel.dismiss()
                viewModel.currentUsername?.let(onOpenAchievements)
            },
            onClose = viewModel::dismiss,
        )
    }
}

@Composable
private fun UnlockBanner(unlocked: UnlockedAchievement, onClick: () -> Unit, onClose: () -> Unit) {
    // Dark in both themes, like the web's grey.900 toast
    Surface(
        onClick = onClick,
        color = Color(0xFF212121),
        contentColor = Color.White,
        shape = MaterialTheme.shapes.medium,
        shadowElevation = 6.dp,
        modifier = Modifier.fillMaxWidth(),
    ) {
        Row(Modifier.padding(start = 16.dp, top = 8.dp, bottom = 8.dp), verticalAlignment = Alignment.CenterVertically) {
            Icon(
                Icons.Filled.EmojiEvents,
                contentDescription = null,
                tint = unlocked.tier.color,
                modifier = Modifier.size(36.dp),
            )
            Spacer(Modifier.width(12.dp))
            Column(Modifier.weight(1f)) {
                Text("Achievement unlocked!", style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.Bold)
                Text("${unlocked.title} (${unlocked.tier.name})", style = MaterialTheme.typography.bodyMedium)
            }
            IconButton(onClick = onClose) { Icon(Icons.Filled.Close, contentDescription = "Close") }
        }
    }
}
