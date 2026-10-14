package com.reviewapp.android.ui.settings

import com.reviewapp.android.util.Validation

// The account settings dialogs' fields and rules (the web's Change*Dialog and SecurityDialog), kept
// free of Android so they can be unit-tested. Errors only show once something was typed.

/** [serverError] is the last failed request's message, cleared when a field changes. */
data class UsernameForm(val current: String, val value: String = current, val serverError: String? = null) {
    val error: String? get() = serverError ?: Validation.usernameError(value)
    /** A case-only change of the own name is allowed, an identical one is pointless. */
    val canSave: Boolean get() = Validation.usernameError(value) == null && value.trim() != current
}

data class EmailForm(
    val newEmail: String = "",
    val confirmEmail: String = "",
    val currentPassword: String = "",
    val serverError: String? = null,
) {
    val emailError: String?
        get() = if (newEmail.isNotBlank() && !Validation.isValidEmail(newEmail.trim())) "Please enter a valid email address." else null
    val confirmError: String?
        get() = if (confirmEmail.isNotBlank() && !Validation.emailsMatch(newEmail, confirmEmail)) "Emails do not match." else null
    val canSave: Boolean
        get() = Validation.isValidEmail(newEmail.trim()) && Validation.emailsMatch(newEmail, confirmEmail) &&
            currentPassword.isNotEmpty() && currentPassword.length <= Validation.MAX_PASSWORD_LENGTH
}

data class PasswordForm(
    val currentPassword: String = "",
    val newPassword: String = "",
    val confirmPassword: String = "",
    val serverError: String? = null,
) {
    val newPasswordError: String?
        get() = when {
            newPassword.isEmpty() -> null
            newPassword.length !in Validation.MIN_PASSWORD_LENGTH..Validation.MAX_PASSWORD_LENGTH ->
                "Must be between ${Validation.MIN_PASSWORD_LENGTH} and ${Validation.MAX_PASSWORD_LENGTH} characters."
            newPassword == currentPassword -> "Must be different from the current password."
            else -> null
        }
    val confirmError: String?
        get() = if (confirmPassword.isNotEmpty() && confirmPassword != newPassword) "Passwords do not match." else null
    val canSave: Boolean
        get() = currentPassword.isNotEmpty() && currentPassword.length <= Validation.MAX_PASSWORD_LENGTH &&
            newPassword.isNotEmpty() && newPasswordError == null && confirmPassword == newPassword
}

/**
 * Turning two-factor authentication on ([enable]) or off: the password step emails a code, the code
 * step confirms it. The password is kept after the first step, "Resend code" requests again with it.
 */
data class TwoFactorFlow(
    val enable: Boolean,
    val step: Step = Step.Password,
    val password: String = "",
    val code: String = "",
    val isSubmitting: Boolean = false,
    val error: String? = null,
    /** The server's "We sent a code..." message. */
    val info: String? = null,
) {
    enum class Step { Password, Code }

    val canRequestCode: Boolean
        get() = !isSubmitting && password.isNotEmpty() && password.length <= Validation.MAX_PASSWORD_LENGTH
    val canConfirm: Boolean get() = !isSubmitting && step == Step.Code && Validation.isValidTwoFactorCode(code)

    fun withPassword(value: String) = copy(password = value, error = null)

    /** Digits only, at most 6. */
    fun withCode(value: String) = copy(code = value.filter(Char::isDigit).take(Validation.TWO_FACTOR_CODE_LENGTH), error = null)

    fun submitting() = copy(isSubmitting = true, error = null)

    fun codeSent(message: String) = copy(step = Step.Code, isSubmitting = false, error = null, info = message, code = "")

    /** A wrong password is cleared; in the code step only the error shows (the code may be retyped). */
    fun failed(message: String) = copy(
        isSubmitting = false,
        error = message,
        info = null,
        password = if (step == Step.Password) "" else password,
    )
}
