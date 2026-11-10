package com.reviewapp.android.ui.settings

import com.reviewapp.android.util.Validation

/*
 * The account settings dialogs' fields and rules (the web's Change*Dialog and SecurityDialog), kept free of Android
 * so they can be unit-tested. Field errors only show once something was typed. Every form's `serverError` is the
 * message of the last failed request and is cleared when a field changes.
 */

/** The change username dialog; [current] is the name now, [value] the text field. */
data class UsernameForm(val current: String, val value: String = current, val serverError: String? = null) {
    /** The request's error, or the rule the typed name breaks. */
    val error: String? get() = serverError ?: Validation.usernameError(value)

    /** A case-only change of the own name is allowed, an identical one is pointless. */
    val canSave: Boolean get() = Validation.usernameError(value) == null && value.trim() != current
}

/** The change email dialog: the new address twice and the current password (the API requires it). */
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

/** The change password dialog: the new password must differ from the current one and be typed twice. */
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
    /** `true` turns 2FA on, `false` off. */
    val enable: Boolean,
    val step: Step = Step.Password,
    val password: String = "",
    val code: String = "",
    val isSubmitting: Boolean = false,
    val error: String? = null,
    /** The server's "We sent a code..." message. */
    val info: String? = null,
) {
    /** The two screens of the flow. */
    enum class Step { Password, Code }

    /** The password step can be submitted, or "Resend code" can be used. */
    val canRequestCode: Boolean
        get() = !isSubmitting && password.isNotEmpty() && password.length <= Validation.MAX_PASSWORD_LENGTH
    /** The code step has a complete code to confirm. */
    val canConfirm: Boolean get() = !isSubmitting && step == Step.Code && Validation.isValidTwoFactorCode(code)

    /** Stores the typed password and clears the error. */
    fun withPassword(value: String) = copy(password = value, error = null)

    /** Digits only, at most 6. */
    fun withCode(value: String) = copy(code = value.filter(Char::isDigit).take(Validation.TWO_FACTOR_CODE_LENGTH), error = null)

    /** A request is running. */
    fun submitting() = copy(isSubmitting = true, error = null)

    /** The code was emailed: moves to the code step and keeps the server's message. */
    fun codeSent(message: String) = copy(step = Step.Code, isSubmitting = false, error = null, info = message, code = "")

    /** A wrong password is cleared; in the code step only the error shows (the code may be retyped). */
    fun failed(message: String) = copy(
        isSubmitting = false,
        error = message,
        info = null,
        password = if (step == Step.Password) "" else password,
    )
}
