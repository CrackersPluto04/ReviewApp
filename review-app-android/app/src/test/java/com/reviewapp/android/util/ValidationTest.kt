package com.reviewapp.android.util

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class ValidationTest {

    @Test
    fun email_matchesWebRules() {
        assertTrue(Validation.isValidEmail("user@example.com"))
        assertTrue(Validation.isValidEmail("first.last@mail.example.co.uk"))
        assertFalse(Validation.isValidEmail("user@example"))
        assertFalse(Validation.isValidEmail("user@.com"))
        assertFalse(Validation.isValidEmail("us er@example.com"))
        assertFalse(Validation.isValidEmail("a".repeat(250) + "@x.com"))
    }

    @Test
    fun username_isTrimmedAndLimited() {
        assertNull(Validation.usernameError("  bob  "))
        assertNotNull(Validation.usernameError(" ab "))
        assertNotNull(Validation.usernameError("a".repeat(21)))
        assertEquals("Username is required.", Validation.usernameError("   "))
    }

    @Test
    fun passwords_respectBcryptSafeLimits() {
        assertNull(Validation.newPasswordError("12345678"))
        assertNotNull(Validation.newPasswordError("1234567"))
        assertNotNull(Validation.newPasswordError("x".repeat(21)))
        // Login only checks the upper limit
        assertNull(Validation.loginPasswordError("short"))
        assertNotNull(Validation.loginPasswordError("x".repeat(21)))
    }

    @Test
    fun confirmPassword_mustMatch() {
        assertNull(Validation.confirmPasswordError("password1", "password1"))
        assertEquals("Passwords do not match.", Validation.confirmPasswordError("password1", "password2"))
    }

    @Test
    fun twoFactorCode_isSixDigits() {
        assertTrue(Validation.isValidTwoFactorCode("012345"))
        assertFalse(Validation.isValidTwoFactorCode("12345"))
        assertFalse(Validation.isValidTwoFactorCode("12345a"))
    }
}
