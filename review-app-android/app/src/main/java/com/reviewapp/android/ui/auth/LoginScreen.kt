package com.reviewapp.android.ui.auth

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Visibility
import androidx.compose.material.icons.filled.VisibilityOff
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import com.reviewapp.android.ui.components.AppLogo
import com.reviewapp.android.ui.components.BackButton
import com.reviewapp.android.ui.theme.AppTheme
import com.reviewapp.android.util.Validation

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun LoginScreen(
    onLoggedIn: () -> Unit,
    onBack: () -> Unit,
    viewModel: LoginViewModel = viewModel(factory = LoginViewModel.Factory),
) {
    val state by viewModel.state.collectAsStateWithLifecycle()

    LaunchedEffect(viewModel) {
        viewModel.loggedIn.collect { onLoggedIn() }
    }

    Scaffold(
        topBar = { TopAppBar(title = {}, navigationIcon = { BackButton(onBack) }) },
    ) { padding ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding)
                .imePadding()
                .verticalScroll(rememberScrollState())
                .padding(horizontal = 24.dp, vertical = 8.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            AppLogo(large = true)
            Text(
                when (state.mode) {
                    AuthMode.Login -> "Login to your account"
                    AuthMode.Register -> "Register account"
                    AuthMode.TwoFactor -> "Two-factor authentication"
                },
                style = MaterialTheme.typography.headlineSmall,
                fontWeight = FontWeight.SemiBold,
                textAlign = TextAlign.Center,
            )

            state.info?.let { Banner(it, isError = false) }
            state.error?.let { Banner(it, isError = true) }

            Column(
                Modifier.widthIn(max = 480.dp).fillMaxWidth(),
                verticalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                if (state.mode == AuthMode.TwoFactor) {
                    TwoFactorForm(state, viewModel)
                } else {
                    CredentialsForm(state, viewModel)
                }
            }
        }
    }
}

@Composable
private fun CredentialsForm(state: LoginUiState, viewModel: LoginViewModel) {
    val isRegister = state.mode == AuthMode.Register
    val focusManager = LocalFocusManager.current
    val nextAction = KeyboardActions(onNext = { focusManager.moveFocus(androidx.compose.ui.focus.FocusDirection.Down) })

    if (isRegister) {
        FormField(
            value = state.username,
            onValueChange = viewModel::onUsernameChange,
            label = "Username *",
            error = state.fieldErrors[AuthField.Username],
            keyboardOptions = KeyboardOptions(imeAction = ImeAction.Next),
            keyboardActions = nextAction,
        )
    }
    FormField(
        value = state.email,
        onValueChange = viewModel::onEmailChange,
        label = "Email *",
        error = state.fieldErrors[AuthField.Email],
        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Email, imeAction = ImeAction.Next),
        keyboardActions = nextAction,
    )
    PasswordField(
        value = state.password,
        onValueChange = viewModel::onPasswordChange,
        label = "Password *",
        error = state.fieldErrors[AuthField.Password],
        imeAction = if (isRegister) ImeAction.Next else ImeAction.Done,
        keyboardActions = if (isRegister) nextAction else KeyboardActions(onDone = { viewModel.submit() }),
    )
    if (isRegister) {
        PasswordField(
            value = state.confirmPassword,
            onValueChange = viewModel::onConfirmPasswordChange,
            label = "Confirm Password *",
            error = state.fieldErrors[AuthField.ConfirmPassword],
            imeAction = ImeAction.Done,
            keyboardActions = KeyboardActions(onDone = { viewModel.submit() }),
        )
    }

    SubmitButton(
        text = if (isRegister) "Register" else "Login",
        isSubmitting = state.isSubmitting,
        enabled = true,
        onClick = viewModel::submit,
    )

    TextButton(
        onClick = { viewModel.switchMode(if (isRegister) AuthMode.Login else AuthMode.Register) },
        modifier = Modifier.fillMaxWidth(),
    ) {
        Text(if (isRegister) "Already have an account? Login here" else "Don't have an account? Register here")
    }
}

@Composable
private fun TwoFactorForm(state: LoginUiState, viewModel: LoginViewModel) {
    FormField(
        value = state.code,
        onValueChange = viewModel::onCodeChange,
        label = "6-digit code",
        error = state.fieldErrors[AuthField.Code],
        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.NumberPassword, imeAction = ImeAction.Done),
        keyboardActions = KeyboardActions(onDone = { viewModel.submit() }),
    )
    SubmitButton(
        text = "Verify and log in",
        isSubmitting = state.isSubmitting,
        enabled = state.code.length == Validation.TWO_FACTOR_CODE_LENGTH,
        onClick = viewModel::submit,
    )
    TextButton(
        onClick = viewModel::resendCode,
        enabled = state.resendSecondsLeft == 0 && !state.isSubmitting,
        modifier = Modifier.fillMaxWidth(),
    ) {
        Text(if (state.resendSecondsLeft > 0) "Resend code (${state.resendSecondsLeft}s)" else "Resend code")
    }
    TextButton(onClick = viewModel::backToLogin, modifier = Modifier.fillMaxWidth()) {
        Text("Back to login")
    }
}

@Composable
private fun FormField(
    value: String,
    onValueChange: (String) -> Unit,
    label: String,
    error: String?,
    keyboardOptions: KeyboardOptions,
    keyboardActions: KeyboardActions,
) {
    OutlinedTextField(
        value = value,
        onValueChange = onValueChange,
        label = { Text(label) },
        isError = error != null,
        supportingText = error?.let { { Text(it) } },
        singleLine = true,
        keyboardOptions = keyboardOptions,
        keyboardActions = keyboardActions,
        modifier = Modifier.fillMaxWidth(),
    )
}

@Composable
private fun PasswordField(
    value: String,
    onValueChange: (String) -> Unit,
    label: String,
    error: String?,
    imeAction: ImeAction,
    keyboardActions: KeyboardActions,
) {
    var visible by rememberSaveable { mutableStateOf(false) }
    OutlinedTextField(
        value = value,
        onValueChange = onValueChange,
        label = { Text(label) },
        isError = error != null,
        supportingText = error?.let { { Text(it) } },
        singleLine = true,
        visualTransformation = if (visible) VisualTransformation.None else PasswordVisualTransformation(),
        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password, imeAction = imeAction),
        keyboardActions = keyboardActions,
        trailingIcon = {
            IconButton(onClick = { visible = !visible }) {
                Icon(
                    if (visible) Icons.Filled.VisibilityOff else Icons.Filled.Visibility,
                    contentDescription = if (visible) "Hide password" else "Show password",
                )
            }
        },
        modifier = Modifier.fillMaxWidth(),
    )
}

/** The green primary button of the wireframes (the web app's "success" button). */
@Composable
private fun SubmitButton(text: String, isSubmitting: Boolean, enabled: Boolean, onClick: () -> Unit) {
    Button(
        onClick = onClick,
        enabled = enabled && !isSubmitting,
        colors = ButtonDefaults.buttonColors(
            containerColor = AppTheme.extendedColors.success,
            contentColor = AppTheme.extendedColors.onSuccess,
        ),
        modifier = Modifier.fillMaxWidth().height(52.dp),
    ) {
        if (isSubmitting) {
            CircularProgressIndicator(Modifier.size(22.dp), strokeWidth = 2.dp, color = AppTheme.extendedColors.onSuccess)
        } else {
            Text(text, style = MaterialTheme.typography.titleMedium)
        }
    }
}

@Composable
private fun Banner(text: String, isError: Boolean) {
    Surface(
        color = if (isError) AppTheme.extendedColors.errorContainer else AppTheme.extendedColors.successContainer,
        contentColor = if (isError) MaterialTheme.colorScheme.error else AppTheme.extendedColors.success,
        shape = MaterialTheme.shapes.medium,
        modifier = Modifier.widthIn(max = 480.dp).fillMaxWidth(),
    ) {
        Text(text, modifier = Modifier.padding(12.dp), style = MaterialTheme.typography.bodyMedium)
    }
}
