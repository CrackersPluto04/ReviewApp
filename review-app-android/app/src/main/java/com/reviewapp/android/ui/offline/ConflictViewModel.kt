package com.reviewapp.android.ui.offline

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.createSavedStateHandle
import androidx.lifecycle.viewModelScope
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory
import androidx.navigation.toRoute
import com.reviewapp.android.data.sync.ConflictResolver
import com.reviewapp.android.data.sync.FieldChoice
import com.reviewapp.android.data.sync.OfflineGateway
import com.reviewapp.android.data.sync.Resolution
import com.reviewapp.android.di.appContainer
import com.reviewapp.android.ui.navigation.ConflictRoute
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.receiveAsFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class ConflictUiState(
    val view: IssueView? = null,
    val isLoaded: Boolean = false,
    /** Synced, discarded or decided meanwhile. */
    val gone: Boolean = false,
    /** Per conflicting field: Mine until the user picks another. */
    val choices: Map<String, FieldChoice> = emptyMap(),
    /** The edited texts of the fields being edited. */
    val edits: Map<String, String> = emptyMap(),
    /** A taken collection name: the new one. */
    val name: String = "",
    val isWorking: Boolean = false,
    val error: String? = null,
    val confirmingDiscard: Boolean = false,
)

/** One change in Sync issues and the user's decision about it ([ConflictResolver] applies it). */
class ConflictViewModel(
    private val seq: Long,
    private val resolver: ConflictResolver,
    private val gateway: OfflineGateway,
) : ViewModel() {

    private val _state = MutableStateFlow(ConflictUiState())
    val state: StateFlow<ConflictUiState> = _state.asStateFlow()

    /** The decision is made: the screen closes. */
    private val _done = Channel<Unit>(Channel.BUFFERED)
    val done = _done.receiveAsFlow()

    init {
        load()
    }

    private fun load() = viewModelScope.launch {
        val db = gateway.database()
        val details = resolver.details(seq)
        if (db == null || details == null) {
            _state.update { it.copy(isLoaded = true, gone = true) }
            return@launch
        }
        val view = issueView(details, describeContextOf(db))
        _state.update {
            it.copy(
                view = view,
                isLoaded = true,
                choices = view.diffs.associate { diff -> diff.field to FieldChoice.Mine },
                name = view.currentName.orEmpty(),
            )
        }
    }

    fun choose(field: String, choice: FieldChoice) = _state.update { state ->
        val diff = state.view?.diffs?.firstOrNull { it.field == field }
        // Editing starts from both texts
        val edits = if (choice is FieldChoice.Edited && field !in state.edits && diff != null) state.edits + (field to diff.combined) else state.edits
        state.copy(choices = state.choices + (field to choice), edits = edits, error = null)
    }

    fun edit(field: String, text: String) = _state.update { state ->
        val max = state.view?.diffs?.firstOrNull { it.field == field }?.maxLength ?: Int.MAX_VALUE
        state.copy(edits = state.edits + (field to text.take(max)), error = null)
    }

    fun setName(name: String) = _state.update { it.copy(name = name, error = null) }

    fun askDiscard(show: Boolean) = _state.update { it.copy(confirmingDiscard = show) }

    fun act(action: IssueAction) {
        if (_state.value.isWorking) return
        _state.update { it.copy(isWorking = true, error = null, confirmingDiscard = false) }
        viewModelScope.launch {
            val state = _state.value
            val result = when (action) {
                IssueAction.SaveChoices -> resolver.resolveFields(
                    seq,
                    state.choices.mapValues { (field, choice) ->
                        if (choice is FieldChoice.Edited) FieldChoice.Edited(state.edits[field].orEmpty()) else choice
                    },
                )
                IssueAction.Recreate -> resolver.recreate(seq)
                IssueAction.DeleteAnyway -> resolver.deleteAnyway(seq)
                IssueAction.KeepMine -> resolver.overwriteWithMine(seq)
                IssueAction.Rename -> resolver.rename(seq, state.name)
                IssueAction.Merge -> resolver.mergeIntoExisting(seq)
                IssueAction.Retry -> resolver.retry(seq)
                IssueAction.UseTheirs -> resolver.useTheirs(seq)
            }
            when (result) {
                Resolution.Done -> _done.send(Unit)
                Resolution.Gone -> _state.update { it.copy(isWorking = false, gone = true) }
                is Resolution.Invalid -> _state.update { it.copy(isWorking = false, error = result.message) }
            }
        }
    }

    companion object {
        val Factory: ViewModelProvider.Factory = viewModelFactory {
            initializer {
                val c = appContainer()
                val route = createSavedStateHandle().toRoute<ConflictRoute>()
                ConflictViewModel(route.seq, c.conflictResolver, c.offlineGateway)
            }
        }
    }
}

