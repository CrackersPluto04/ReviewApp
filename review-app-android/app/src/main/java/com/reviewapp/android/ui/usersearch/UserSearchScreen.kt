package com.reviewapp.android.ui.usersearch

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Close
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.ListItem
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextField
import androidx.compose.material3.TextFieldDefaults
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.unit.dp
import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewModelScope
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory
import com.reviewapp.android.data.remote.ApiResult
import com.reviewapp.android.data.repository.UserRepository
import com.reviewapp.android.di.appContainer
import com.reviewapp.android.domain.model.UserSummary
import com.reviewapp.android.ui.components.BackButton
import com.reviewapp.android.ui.components.EmptyState
import com.reviewapp.android.ui.components.ErrorState
import com.reviewapp.android.ui.components.UserAvatar
import kotlinx.coroutines.FlowPreview
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.flow.debounce
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class UserSearchUiState(
    val query: String = "",
    val results: List<UserSummary> = emptyList(),
    val isLoading: Boolean = false,
    val error: String? = null,
    /** True once a search for the current text finished, so "no users" isn't shown while typing. */
    val searched: Boolean = false,
)

/** The web's `UserSearchBar`: searches 300 ms after typing stops, up to 10 users whose name contains the text. */
@OptIn(FlowPreview::class)
class UserSearchViewModel(private val userRepository: UserRepository) : ViewModel() {

    private val _state = MutableStateFlow(UserSearchUiState())
    val state: StateFlow<UserSearchUiState> = _state.asStateFlow()

    init {
        viewModelScope.launch {
            _state.map { it.query.trim() }
                .distinctUntilChanged()
                .debounce(300)
                .collectLatest { query -> search(query) }
        }
    }

    private suspend fun search(query: String) {
        if (query.isEmpty()) {
            _state.update { it.copy(results = emptyList(), isLoading = false, error = null, searched = false) }
            return
        }
        _state.update { it.copy(isLoading = true, error = null) }
        when (val result = userRepository.search(query)) {
            is ApiResult.Success -> _state.update { it.copy(results = result.data, isLoading = false, searched = true) }
            is ApiResult.Error -> _state.update { it.copy(isLoading = false, error = result.message, searched = true) }
        }
    }

    fun updateQuery(query: String) = _state.update { it.copy(query = query, searched = false) }

    fun retry() {
        viewModelScope.launch { search(_state.value.query.trim()) }
    }

    companion object {
        val Factory: ViewModelProvider.Factory = viewModelFactory {
            initializer { UserSearchViewModel(appContainer().userRepository) }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun UserSearchScreen(
    onBack: () -> Unit,
    onOpenProfile: (String) -> Unit,
    viewModel: UserSearchViewModel = viewModel(factory = UserSearchViewModel.Factory),
) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    val focusRequester = remember { FocusRequester() }
    val keyboard = LocalSoftwareKeyboardController.current

    LaunchedEffect(Unit) { focusRequester.requestFocus() }

    Scaffold(
        topBar = {
            TopAppBar(
                navigationIcon = { BackButton(onBack) },
                title = {
                    TextField(
                        value = state.query,
                        onValueChange = viewModel::updateQuery,
                        placeholder = { Text("Find users...") },
                        singleLine = true,
                        trailingIcon = {
                            when {
                                state.isLoading -> CircularProgressIndicator(Modifier.size(20.dp), strokeWidth = 2.dp)
                                state.query.isNotEmpty() -> IconButton(onClick = { viewModel.updateQuery("") }) {
                                    Icon(Icons.Filled.Close, contentDescription = "Clear")
                                }
                            }
                        },
                        keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
                        keyboardActions = KeyboardActions(onSearch = { keyboard?.hide() }),
                        colors = TextFieldDefaults.colors(
                            focusedContainerColor = Color.Transparent,
                            unfocusedContainerColor = Color.Transparent,
                        ),
                        modifier = Modifier.fillMaxWidth().focusRequester(focusRequester),
                    )
                },
            )
        },
    ) { padding ->
        val modifier = Modifier.fillMaxSize().padding(padding)
        when {
            state.error != null -> ErrorState(state.error!!, viewModel::retry, modifier)
            state.searched && state.results.isEmpty() -> EmptyState("No users found.", modifier)
            state.query.isBlank() -> EmptyState("Search for other users by their username.", modifier)
            else -> LazyColumn(modifier) {
                items(state.results, key = { it.id }) { user ->
                    ListItem(
                        headlineContent = { Text(user.username, fontWeight = FontWeight.SemiBold) },
                        leadingContent = { UserAvatar(user.username, user.profilePictureUrl) },
                        modifier = Modifier.clickable { onOpenProfile(user.username) },
                    )
                }
            }
        }
    }
}
