package com.reviewapp.android.ui.settings

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.Logout
import androidx.compose.material.icons.outlined.AlternateEmail
import androidx.compose.material.icons.outlined.Badge
import androidx.compose.material.icons.outlined.Devices
import androidx.compose.material.icons.outlined.Info
import androidx.compose.material.icons.outlined.Key
import androidx.compose.material.icons.outlined.MarkEmailRead
import androidx.compose.material.icons.outlined.Password
import androidx.compose.material.icons.outlined.Security
import androidx.compose.material.icons.outlined.WorkspacePremium
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Card
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.ListItem
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.LifecycleResumeEffect
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import com.reviewapp.android.domain.model.SecurityStatus
import com.reviewapp.android.ui.components.BackButton
import com.reviewapp.android.ui.components.ErrorState
import com.reviewapp.android.ui.components.LoadingState
import com.reviewapp.android.ui.components.PremiumBadge
import com.reviewapp.android.ui.components.PremiumUpsell
import com.reviewapp.android.ui.theme.AppTheme

/** The owner's account settings, opened from the profile's settings icon. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun AccountSettingsScreen(
    onBack: () -> Unit,
    onOpenPremium: () -> Unit,
    viewModel: AccountSettingsViewModel = viewModel(factory = AccountSettingsViewModel.Factory),
) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    val snackbarHostState = remember { SnackbarHostState() }
    val currentOnBack by rememberUpdatedState(onBack)

    LaunchedEffect(viewModel) {
        viewModel.events.collect { event ->
            when (event) {
                is AccountSettingsEvent.Message -> snackbarHostState.showSnackbar(event.text)
                AccountSettingsEvent.LoggedOut -> currentOnBack()
            }
        }
    }
    // The email may have been verified or changed in the browser meanwhile
    LifecycleResumeEffect(viewModel) {
        viewModel.onResume()
        onPauseOrDispose { }
    }

    Scaffold(
        topBar = { TopAppBar(title = { Text("Account settings") }, navigationIcon = { BackButton(onBack) }) },
        snackbarHost = { SnackbarHost(snackbarHostState) },
    ) { padding ->
        val status = state.status
        val modifier = Modifier.fillMaxSize().padding(padding)
        when {
            status == null && state.isLoading -> Box(modifier) { LoadingState() }
            status == null -> Box(modifier) {
                ErrorState(state.error ?: "Could not load your account settings.", { viewModel.loadStatus() }, Modifier.fillMaxWidth())
            }
            else -> SettingsList(state, status, viewModel, onOpenPremium, modifier)
        }
    }

    AccountDialogs(state, viewModel)
}

@Composable
private fun SettingsList(
    state: AccountSettingsUiState,
    status: SecurityStatus,
    viewModel: AccountSettingsViewModel,
    onOpenPremium: () -> Unit,
    modifier: Modifier,
) {
    LazyColumn(modifier, contentPadding = PaddingValues(bottom = 24.dp)) {
        item { SectionHeader("Account") }
        item {
            SettingsItem(
                icon = Icons.Outlined.Badge,
                title = "Username",
                subtitle = state.username,
                onClick = viewModel::openUsername,
            )
        }
        item {
            SettingsItem(
                icon = Icons.Outlined.AlternateEmail,
                title = "Email",
                subtitle = status.email + (status.pendingEmail?.let { "\nPending: $it" } ?: ""),
                onClick = viewModel::openEmail,
            )
        }
        item {
            SettingsItem(
                icon = Icons.Outlined.Password,
                title = "Password",
                subtitle = "Change your password",
                onClick = viewModel::openPassword,
            )
        }

        item { SectionDivider() }
        item { SectionHeader("Security") }
        item { VerificationItem(state, status, viewModel::resendVerification) }
        status.pendingEmail?.let { pending ->
            item { PendingEmailCard(pending, enabled = !state.isSubmitting, onCancel = viewModel::cancelPendingEmail) }
        }
        item { TwoFactorItem(status, onOpen = viewModel::openTwoFactor) }

        item { SectionDivider() }
        item { SectionHeader("Subscription") }
        item {
            if (state.isPremium) {
                SettingsItem(
                    icon = Icons.Outlined.WorkspacePremium,
                    title = "Premium",
                    subtitle = "Manage your subscription and payment details",
                    onClick = onOpenPremium,
                    trailing = { PremiumBadge() },
                )
            } else {
                PremiumUpsell(
                    onSeePremium = onOpenPremium,
                    title = "You're on the Free plan",
                    message = "Subscribe to Premium to unlock exclusive features.",
                    modifier = Modifier.padding(horizontal = 16.dp, vertical = 4.dp),
                )
            }
        }

        item { SectionDivider() }
        item { SectionHeader("Sessions") }
        item {
            SettingsItem(
                icon = Icons.AutoMirrored.Filled.Logout,
                title = "Log out",
                subtitle = "Log out on this device",
                onClick = viewModel::logout,
            )
        }
        item {
            SettingsItem(
                icon = Icons.Outlined.Devices,
                title = "Log out of all devices",
                subtitle = "Every device, this one included",
                onClick = viewModel::askLogoutAll,
                color = MaterialTheme.colorScheme.error,
            )
        }
    }
}

@Composable
private fun SectionHeader(text: String) {
    Text(
        text,
        style = MaterialTheme.typography.titleSmall,
        color = MaterialTheme.colorScheme.primary,
        modifier = Modifier.padding(start = 16.dp, end = 16.dp, top = 16.dp, bottom = 4.dp),
    )
}

@Composable
private fun SectionDivider() = HorizontalDivider(Modifier.padding(top = 8.dp))

@Composable
private fun SettingsItem(
    icon: androidx.compose.ui.graphics.vector.ImageVector,
    title: String,
    subtitle: String?,
    onClick: (() -> Unit)?,
    color: Color = Color.Unspecified,
    trailing: (@Composable () -> Unit)? = null,
) {
    ListItem(
        headlineContent = { Text(title, color = color) },
        supportingContent = subtitle?.let { { Text(it) } },
        leadingContent = {
            Icon(icon, contentDescription = null, tint = if (color == Color.Unspecified) MaterialTheme.colorScheme.onSurfaceVariant else color)
        },
        trailingContent = trailing,
        modifier = if (onClick != null) Modifier.clickable(onClick = onClick) else Modifier,
    )
}

/** A small coloured label, the web's status chips. */
@Composable
private fun StatusChip(text: String, positive: Boolean) {
    val color = if (positive) AppTheme.extendedColors.success else AppTheme.extendedColors.warning
    Surface(
        color = color.copy(alpha = 0.15f),
        contentColor = color,
        shape = MaterialTheme.shapes.small,
    ) {
        Text(
            text,
            style = MaterialTheme.typography.labelMedium,
            fontWeight = FontWeight.SemiBold,
            modifier = Modifier.padding(horizontal = 8.dp, vertical = 4.dp),
        )
    }
}

@Composable
private fun VerificationItem(state: AccountSettingsUiState, status: SecurityStatus, onResend: () -> Unit) {
    Column {
        SettingsItem(
            icon = Icons.Outlined.MarkEmailRead,
            title = "Email verification",
            subtitle = if (status.isEmailConfirmed) {
                status.email
            } else {
                "Verify your address to enable two-factor authentication and to change your email."
            },
            onClick = null,
            trailing = { StatusChip(if (status.isEmailConfirmed) "Verified" else "Not verified", status.isEmailConfirmed) },
        )
        if (!status.isEmailConfirmed) {
            val seconds = state.verificationResendSecondsLeft
            OutlinedButton(
                onClick = onResend,
                enabled = seconds == 0 && !state.isSubmitting,
                modifier = Modifier.padding(start = 56.dp, end = 16.dp, bottom = 8.dp),
            ) {
                Text(if (seconds > 0) "Send verification email (${seconds}s)" else "Send verification email")
            }
        }
    }
}

@Composable
private fun PendingEmailCard(pendingEmail: String, enabled: Boolean, onCancel: () -> Unit) {
    Card(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 4.dp)) {
        Row(Modifier.padding(start = 16.dp, top = 12.dp, end = 8.dp, bottom = 4.dp)) {
            Icon(Icons.Outlined.Info, contentDescription = null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.padding(top = 2.dp, end = 12.dp))
            Text(
                buildAnnotatedString {
                    append("Change to ")
                    withStyle(MaterialTheme.typography.bodyMedium.toSpanStyle().copy(fontWeight = FontWeight.Bold)) { append(pendingEmail) }
                    append(" is waiting for confirmation. Open the link we sent to that address.")
                },
                style = MaterialTheme.typography.bodyMedium,
                modifier = Modifier.weight(1f),
            )
        }
        TextButton(onClick = onCancel, enabled = enabled, modifier = Modifier.align(Alignment.End).padding(end = 8.dp)) {
            Text("Cancel change")
        }
    }
}

@Composable
private fun TwoFactorItem(status: SecurityStatus, onOpen: (enable: Boolean) -> Unit) {
    Column {
        SettingsItem(
            icon = Icons.Outlined.Security,
            title = "Two-factor authentication",
            subtitle = "When it's on, logging in also requires a 6-digit code that we send to your email address.",
            onClick = null,
            trailing = { StatusChip(if (status.isTwoFactorEnabled) "On" else "Off", status.isTwoFactorEnabled) },
        )
        Column(
            Modifier.padding(start = 56.dp, end = 16.dp, bottom = 8.dp),
            verticalArrangement = Arrangement.spacedBy(4.dp),
        ) {
            if (status.isTwoFactorEnabled) {
                OutlinedButton(
                    onClick = { onOpen(false) },
                    colors = ButtonDefaults.outlinedButtonColors(contentColor = MaterialTheme.colorScheme.error),
                ) {
                    Text("Disable two-factor authentication")
                }
            } else {
                Button(onClick = { onOpen(true) }, enabled = status.isEmailConfirmed) {
                    Icon(Icons.Outlined.Key, contentDescription = null, modifier = Modifier.padding(end = 8.dp))
                    Text("Enable two-factor authentication")
                }
                if (!status.isEmailConfirmed) {
                    Text(
                        "Verify your email address first.",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
            }
        }
    }
}
