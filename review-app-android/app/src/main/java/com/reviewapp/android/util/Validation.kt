package com.reviewapp.android.util

import java.net.URI
import java.net.URISyntaxException

/**
 * Client-side validation, mirrors `review-app/src/utils/validation.ts` and the API's DTO limits.
 * Kotlin, JS and C# all count UTF-16 units, so the lengths match the server exactly.
 */
object Validation {
    const val MAX_EMAIL_LENGTH = 254
    const val MIN_PASSWORD_LENGTH = 8
    const val MAX_PASSWORD_LENGTH = 20
    const val MIN_USERNAME_LENGTH = 3
    const val MAX_USERNAME_LENGTH = 20
    const val TWO_FACTOR_CODE_LENGTH = 6
    const val MAX_REVIEW_LENGTH = 500
    const val MAX_PROS_CONS_LENGTH = 250
    const val MAX_BIO_LENGTH = 150
    const val MAX_COLLECTION_NAME_LENGTH = 50
    const val MAX_REPLY_LENGTH = 500

    private val EMAIL_REGEX = Regex("^[^\\s@]+@[^\\s@.]+(\\.[^\\s@.]+)+$")
    private val TWO_FACTOR_CODE_REGEX = Regex("^\\d{6}$")

    fun isValidEmail(email: String): Boolean =
        email.length <= MAX_EMAIL_LENGTH && EMAIL_REGEX.matches(email)

    fun isValidTwoFactorCode(code: String): Boolean = TWO_FACTOR_CODE_REGEX.matches(code)

    fun usernameError(username: String): String? {
        val trimmed = username.trim()
        return when {
            trimmed.isEmpty() -> "Username is required."
            trimmed.length !in MIN_USERNAME_LENGTH..MAX_USERNAME_LENGTH ->
                "Username must be between $MIN_USERNAME_LENGTH and $MAX_USERNAME_LENGTH characters."
            else -> null
        }
    }

    fun emailError(email: String): String? = when {
        email.isBlank() -> "Email is required."
        !isValidEmail(email.trim()) -> "Please enter a valid email address."
        else -> null
    }

    /** Login only checks the upper limit, the lower one may have changed since the account was made. */
    fun loginPasswordError(password: String): String? = when {
        password.isEmpty() -> "Password is required."
        password.length > MAX_PASSWORD_LENGTH -> "Password can be at most $MAX_PASSWORD_LENGTH characters."
        else -> null
    }

    fun newPasswordError(password: String): String? = when {
        password.isEmpty() -> "Password is required."
        password.length !in MIN_PASSWORD_LENGTH..MAX_PASSWORD_LENGTH ->
            "Password must be between $MIN_PASSWORD_LENGTH and $MAX_PASSWORD_LENGTH characters."
        else -> null
    }

    fun collectionNameError(name: String): String? {
        val trimmed = name.trim()
        return when {
            trimmed.isEmpty() -> "Collection name cannot be empty."
            trimmed.length > MAX_COLLECTION_NAME_LENGTH ->
                "Collection name can be at most $MAX_COLLECTION_NAME_LENGTH characters."
            else -> null
        }
    }

    /** Same as the web's `isValidReply`: 1-500 characters after trimming (the API trims too). */
    fun replyError(text: String): String? {
        val length = text.trim().length
        return when {
            length == 0 -> "Reply cannot be empty."
            length > MAX_REPLY_LENGTH -> "Reply can be at most $MAX_REPLY_LENGTH characters."
            else -> null
        }
    }

    /** An absolute http(s) link, the API checks it with `[Url]` too. */
    fun isValidImageUrl(url: String): Boolean {
        val uri = try {
            URI(url.trim())
        } catch (_: URISyntaxException) {
            return false
        }
        return uri.scheme?.lowercase() in setOf("http", "https") && !uri.host.isNullOrBlank()
    }

    fun confirmPasswordError(password: String, confirm: String): String? = when {
        confirm.isEmpty() -> "Please confirm your password."
        confirm != password -> "Passwords do not match."
        else -> null
    }
}
