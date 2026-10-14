package com.reviewapp.android.ui.settings

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import com.reviewapp.android.ui.components.MessageBanner
import com.reviewapp.android.ui.components.PasswordField
import com.reviewapp.android.ui.theme.AppTheme
import com.reviewapp.android.util.Validation

/** The settings screen's open dialog, if any. */
@Composable
fun AccountDialogs(state: AccountSettingsUiState, viewModel: AccountSettingsViewModel) {
    when (val dialog = state.dialog) {
        null -> Unit
        is SettingsDialog.Username -> UsernameDialog(dialog.form, state.isSubmitting, viewModel)
        is SettingsDialog.Email -> EmailDialog(dialog.form, state.isSubmitting, viewModel)
        is SettingsDialog.Password -> PasswordDialog(dialog.form, state.isSubmitting, viewModel)
        is SettingsDialog.TwoFactor -> TwoFactorDialog(dialog.flow, state.codeResendSecondsLeft, viewModel)
        SettingsDialog.LogoutAll -> LogoutAllDialog(state.isSubmitting, state.dialogError, viewModel)
    }
}

/** An AlertDialog with a scrollable body and a confirm button that shows progress. */
@Composable
private fun FormDialog(
    title: String,
    confirmText: String,
    confirmEnabled: Boolean,
    isSubmitting: Boolean,
    onConfirm: () -> Unit,
    onDismiss: () -> Unit,
    destructive: Boolean = false,
    extraButton: (@Composable () -> Unit)? = null,
    content: @Composable () -> Unit,
) {
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(title) },
        text = {
            Column(Modifier.verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                content()
            }
        },
        confirmButton = {
            Row {
                extraButton?.invoke()
                TextButton(onClick = onConfirm, enabled = confirmEnabled && !isSubmitting) {
                    if (isSubmitting) {
                        CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp)
                    } else {
                        Text(confirmText, color = if (destructive) MaterialTheme.colorScheme.error else Color.Unspecified)
                    }
                }
            }
        },
        dismissButton = { TextButton(onClick = onDismiss, enabled = !isSubmitting) { Text("Cancel") } },
    )
}

@Composable
private fun Hint(text: String) {
    Text(text, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
}

@Composable
private fun UsernameDialog(form: UsernameForm, isSubmitting: Boolean, viewModel: AccountSettingsViewModel) {
    FormDialog(
        title = "Change username",
        confirmText = "Save",
        confirmEnabled = form.canSave,
        isSubmitting = isSubmitting,
        onConfirm = viewModel::saveUsername,
        onDismiss = viewModel::dismissDialog,
    ) {
        Hint("Current username: ${form.current}")
        val error = form.error
        OutlinedTextField(
            value = form.value,
            onValueChange = viewModel::updateUsername,
            label = { Text("New username") },
            singleLine = true,
            isError = error != null,
            supportingText = {
                Row {
                    Text(error ?: "Must be between 3 and 20 characters.", modifier = Modifier.weight(1f))
                    Text("${form.value.trim().length} / ${Validation.MAX_USERNAME_LENGTH}", textAlign = TextAlign.End)
                }
            },
            keyboardOptions = KeyboardOptions(imeAction = ImeAction.Done),
            keyboardActions = KeyboardActions(onDone = { viewModel.saveUsername() }),
            modifier = Modifier.fillMaxWidth(),
        )
    }
}

@Composable
private fun EmailDialog(form: EmailForm, isSubmitting: Boolean, viewModel: AccountSettingsViewModel) {
    FormDialog(
        title = "Change email",
        confirmText = "Save",
        confirmEnabled = form.canSave,
        isSubmitting = isSubmitting,
        onConfirm = viewModel::saveEmail,
        onDismiss = viewModel::dismissDialog,
    ) {
        Hint(
            "Enter your new email address and your current password to confirm it's you. We'll send a " +
                "confirmation link to the new address, your email changes once you open it.",
        )
        form.serverError?.let { MessageBanner(it, isError = true) }
        EmailField(form.newEmail, { v -> viewModel.updateEmailForm { it.copy(newEmail = v) } }, "New email", form.emailError)
        EmailField(form.confirmEmail, { v -> viewModel.updateEmailForm { it.copy(confirmEmail = v) } }, "Confirm new email", form.confirmError)
        PasswordField(
            value = form.currentPassword,
            onValueChange = { v -> viewModel.updateEmailForm { it.copy(currentPassword = v) } },
            label = "Current password",
            imeAction = ImeAction.Done,
            keyboardActions = KeyboardActions(onDone = { viewModel.saveEmail() }),
        )
    }
}

@Composable
private fun EmailField(value: String, onValueChange: (String) -> Unit, label: String, error: String?) {
    OutlinedTextField(
        value = value,
        onValueChange = { if (it.length <= Validation.MAX_EMAIL_LENGTH) onValueChange(it) },
        label = { Text(label) },
        singleLine = true,
        isError = error != null,
        supportingText = error?.let { { Text(it) } },
        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Email, imeAction = ImeAction.Next),
        modifier = Modifier.fillMaxWidth(),
    )
}

@Composable
private fun PasswordDialog(form: PasswordForm, isSubmitting: Boolean, viewModel: AccountSettingsViewModel) {
    FormDialog(
        title = "Change password",
        confirmText = "Save",
        confirmEnabled = form.canSave,
        isSubmitting = isSubmitting,
        onConfirm = viewModel::savePassword,
        onDismiss = viewModel::dismissDialog,
    ) {
        Hint("You will stay logged in on this device. Any other device will be logged out.")
        form.serverError?.let { MessageBanner(it, isError = true) }
        val max = Validation.MAX_PASSWORD_LENGTH
        PasswordField(
            value = form.currentPassword,
            onValueChange = { v -> if (v.length <= max) viewModel.updatePasswordForm { it.copy(currentPassword = v) } },
            label = "Current password",
        )
        PasswordField(
            value = form.newPassword,
            onValueChange = { v -> if (v.length <= max) viewModel.updatePasswordForm { it.copy(newPassword = v) } },
            label = "New password",
            error = form.newPasswordError,
            helper = "Must be between 8 and 20 characters.",
        )
        PasswordField(
            value = form.confirmPassword,
            onValueChange = { v -> if (v.length <= max) viewModel.updatePasswordForm { it.copy(confirmPassword = v) } },
            label = "Confirm new password",
            error = form.confirmError,
            imeAction = ImeAction.Done,
            keyboardActions = KeyboardActions(onDone = { viewModel.savePassword() }),
        )
    }
}

@Composable
private fun TwoFactorDialog(flow: TwoFactorFlow, resendSecondsLeft: Int, viewModel: AccountSettingsViewModel) {
    val action = if (flow.enable) "Enable" else "Disable"
    val inCodeStep = flow.step == TwoFactorFlow.Step.Code
    FormDialog(
        title = "$action two-factor authentication",
        confirmText = if (inCodeStep) action else "Send code",
        confirmEnabled = if (inCodeStep) flow.canConfirm else flow.canRequestCode,
        isSubmitting = flow.isSubmitting,
        onConfirm = if (inCodeStep) viewModel::confirmTwoFactor else viewModel::requestTwoFactorCode,
        onDismiss = viewModel::dismissDialog,
        destructive = !flow.enable && inCodeStep,
        extraButton = if (inCodeStep) {
            {
                TextButton(onClick = viewModel::requestTwoFactorCode, enabled = resendSecondsLeft == 0 && !flow.isSubmitting) {
                    Text(if (resendSecondsLeft > 0) "Resend (${resendSecondsLeft}s)" else "Resend code")
                }
            }
        } else {
            null
        },
    ) {
        if (flow.enable) {
            Text(
                "If you lose access to your email address, you won't be able to log in. Recovery codes aren't available yet.",
                style = MaterialTheme.typography.bodyMedium,
                color = AppTheme.extendedColors.warning,
            )
        }
        flow.info?.let { MessageBanner(it, isError = false) }
        flow.error?.let { MessageBanner(it, isError = true) }
        if (inCodeStep) {
            Hint("Enter the 6-digit code from the email. It expires in 10 minutes.")
            OutlinedTextField(
                value = flow.code,
                onValueChange = viewModel::updateTwoFactorCode,
                label = { Text("Code") },
                singleLine = true,
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.NumberPassword, imeAction = ImeAction.Done),
                keyboardActions = KeyboardActions(onDone = { viewModel.confirmTwoFactor() }),
                modifier = Modifier.fillMaxWidth(),
            )
        } else {
            Hint("Enter your current password, we'll email you a code.")
            PasswordField(
                value = flow.password,
                onValueChange = { if (it.length <= Validation.MAX_PASSWORD_LENGTH) viewModel.updateTwoFactorPassword(it) },
                label = "Current password",
                imeAction = ImeAction.Done,
                keyboardActions = KeyboardActions(onDone = { viewModel.requestTwoFactorCode() }),
            )
        }
    }
}

@Composable
private fun LogoutAllDialog(isSubmitting: Boolean, error: String?, viewModel: AccountSettingsViewModel) {
    FormDialog(
        title = "Log out of all devices",
        confirmText = "Log out everywhere",
        confirmEnabled = true,
        isSubmitting = isSubmitting,
        onConfirm = viewModel::logoutAll,
        onDismiss = viewModel::dismissDialog,
        destructive = true,
    ) {
        Hint(
            "Every device where you are logged in, including this one, will be logged out. Use this if " +
                "you think someone else has access to your account.",
        )
        error?.let { MessageBanner(it, isError = true) }
    }
}
