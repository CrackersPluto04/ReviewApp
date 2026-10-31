package com.reviewapp.android.ui.subscription

import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.WorkspacePremium
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewModelScope
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory
import com.reviewapp.android.data.repository.SubscriptionRepository
import com.reviewapp.android.di.appContainer
import com.reviewapp.android.ui.components.PremiumGold
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

/** Activity-scoped: any 402 from the API (a locked Premium feature) opens the upsell, like the web's PremiumUpsellContext. */
class PremiumRequiredViewModel(repository: SubscriptionRepository) : ViewModel() {

    private val _visible = MutableStateFlow(false)
    val visible: StateFlow<Boolean> = _visible.asStateFlow()

    init {
        viewModelScope.launch { repository.premiumRequired.collect { _visible.value = true } }
    }

    fun dismiss() {
        _visible.value = false
    }

    companion object {
        val Factory: ViewModelProvider.Factory = viewModelFactory {
            initializer { PremiumRequiredViewModel(appContainer().subscriptionRepository) }
        }
    }
}

@Composable
fun PremiumRequiredHost(
    onSeePremium: () -> Unit,
    viewModel: PremiumRequiredViewModel = viewModel(factory = PremiumRequiredViewModel.Factory),
) {
    val visible by viewModel.visible.collectAsStateWithLifecycle()
    if (!visible) return

    AlertDialog(
        onDismissRequest = viewModel::dismiss,
        icon = { Icon(Icons.Filled.WorkspacePremium, contentDescription = null, tint = PremiumGold) },
        title = { Text("This is a Premium feature") },
        text = { Text("Subscribe to Premium to unlock it, along with every other Premium feature. You can cancel any time.") },
        confirmButton = {
            Button(onClick = {
                viewModel.dismiss()
                onSeePremium()
            }) { Text("See Premium") }
        },
        dismissButton = { TextButton(onClick = viewModel::dismiss) { Text("Not now") } },
    )
}
