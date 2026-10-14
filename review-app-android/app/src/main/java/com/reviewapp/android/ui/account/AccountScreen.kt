package com.reviewapp.android.ui.account

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.Logout
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Card
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SegmentedButton
import androidx.compose.material3.SegmentedButtonDefaults
import androidx.compose.material3.SingleChoiceSegmentedButtonRow
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
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
import com.reviewapp.android.data.local.SettingsStore
import com.reviewapp.android.data.local.ThemeMode
import com.reviewapp.android.di.appContainer
import com.reviewapp.android.ui.components.LoadingState
import com.reviewapp.android.ui.components.UserAvatar
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch

class AccountViewModel(
    private val sessionManager: SessionManager,
    private val settingsStore: SettingsStore,
) : ViewModel() {
    val authState: StateFlow<AuthState> = sessionManager.state

    val themeMode: StateFlow<ThemeMode> = settingsStore.themeMode
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), ThemeMode.System)

    fun setThemeMode(mode: ThemeMode) {
        viewModelScope.launch { settingsStore.setThemeMode(mode) }
    }

    fun logout() {
        viewModelScope.launch { sessionManager.logout() }
    }

    companion object {
        val Factory: ViewModelProvider.Factory = viewModelFactory {
            initializer {
                val c = appContainer()
                AccountViewModel(c.sessionManager, c.settingsStore)
            }
        }
    }
}

/** Profile tab for now: who is logged in, theme and logout. The real profile pages come later. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun AccountScreen(
    onLogin: () -> Unit,
    viewModel: AccountViewModel = viewModel(factory = AccountViewModel.Factory),
) {
    val authState by viewModel.authState.collectAsStateWithLifecycle()
    val themeMode by viewModel.themeMode.collectAsStateWithLifecycle()

    Scaffold(topBar = { TopAppBar(title = { Text("Profile") }) }) { padding ->
        Column(
            Modifier
                .fillMaxSize()
                .padding(padding)
                .verticalScroll(rememberScrollState())
                .padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            when (val auth = authState) {
                AuthState.Loading -> LoadingState()
                AuthState.LoggedOut -> Card(Modifier.fillMaxWidth()) {
                    Column(
                        Modifier.padding(16.dp).fillMaxWidth(),
                        horizontalAlignment = Alignment.CenterHorizontally,
                        verticalArrangement = Arrangement.spacedBy(12.dp),
                    ) {
                        Text("You're not logged in", style = MaterialTheme.typography.titleMedium)
                        Text(
                            "Log in to write reviews and see your profile.",
                            style = MaterialTheme.typography.bodyMedium,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                        Button(onClick = onLogin, modifier = Modifier.fillMaxWidth()) { Text("Login / Register") }
                    }
                }
                is AuthState.LoggedIn -> Card(Modifier.fillMaxWidth()) {
                    Row(Modifier.padding(16.dp), verticalAlignment = Alignment.CenterVertically) {
                        UserAvatar(auth.user.username, auth.user.profilePictureUrl, size = 56.dp)
                        Spacer(Modifier.width(16.dp))
                        Column {
                            Text(auth.user.username, style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold)
                            Text(
                                if (auth.user.isEmailConfirmed) "Email verified" else "Email not verified yet",
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                        }
                    }
                }
            }

            Text("Theme", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
            SingleChoiceSegmentedButtonRow(Modifier.fillMaxWidth()) {
                ThemeMode.entries.forEachIndexed { index, mode ->
                    SegmentedButton(
                        selected = themeMode == mode,
                        onClick = { viewModel.setThemeMode(mode) },
                        shape = SegmentedButtonDefaults.itemShape(index, ThemeMode.entries.size),
                    ) { Text(mode.label) }
                }
            }

            if (authState is AuthState.LoggedIn) {
                OutlinedButton(
                    onClick = viewModel::logout,
                    colors = ButtonDefaults.outlinedButtonColors(contentColor = MaterialTheme.colorScheme.error),
                    modifier = Modifier.fillMaxWidth(),
                ) {
                    Icon(Icons.AutoMirrored.Filled.Logout, contentDescription = null)
                    Spacer(Modifier.width(8.dp))
                    Text("Logout")
                }
            }
        }
    }
}
