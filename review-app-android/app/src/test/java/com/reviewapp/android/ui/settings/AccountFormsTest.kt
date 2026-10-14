package com.reviewapp.android.ui.settings

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class AccountFormsTest {

    @Test
    fun username_needsValidChangedName() {
        val form = UsernameForm(current = "alice")
        assertFalse(form.canSave)
        assertNull(form.error)
        assertTrue(form.copy(value = "  alice2 ").canSave)
        // Only the case changes: the API allows it for the own name
        assertTrue(form.copy(value = "Alice").canSave)
        assertFalse(form.copy(value = "al").canSave)
        assertEquals("Username is required.", form.copy(value = "  ").error)
        assertEquals("Username already taken.", form.copy(value = "bob", serverError = "Username already taken.").error)
    }

    @Test
    fun email_confirmationIsCaseInsensitive() {
        val form = EmailForm(newEmail = "New@Example.com", confirmEmail = " new@example.com", currentPassword = "secret12")
        assertTrue(form.canSave)
        assertNull(form.emailError)
        assertNull(form.confirmError)

        assertEquals("Emails do not match.", form.copy(confirmEmail = "other@example.com").confirmError)
        assertEquals("Please enter a valid email address.", form.copy(newEmail = "nope").emailError)
        assertFalse(form.copy(currentPassword = "").canSave)
        // Nothing typed yet: no errors, but nothing to save either
        assertNull(EmailForm().emailError)
        assertFalse(EmailForm().canSave)
    }

    @Test
    fun password_mustBeNewValidAndConfirmed() {
        val form = PasswordForm(currentPassword = "oldpass12", newPassword = "newpass12", confirmPassword = "newpass12")
        assertTrue(form.canSave)

        assertEquals("Must be different from the current password.", form.copy(newPassword = "oldpass12", confirmPassword = "oldpass12").newPasswordError)
        assertEquals("Must be between 8 and 20 characters.", form.copy(newPassword = "short").newPasswordError)
        assertEquals("Passwords do not match.", form.copy(confirmPassword = "newpass13").confirmError)
        assertFalse(form.copy(confirmPassword = "newpass13").canSave)
        assertFalse(form.copy(currentPassword = "").canSave)
    }

    @Test
    fun twoFactor_passwordThenCode() {
        var flow = TwoFactorFlow(enable = true)
        assertFalse(flow.canRequestCode)
        flow = flow.withPassword("secret12")
        assertTrue(flow.canRequestCode)

        flow = flow.submitting()
        assertFalse(flow.canRequestCode)
        flow = flow.codeSent("We sent a 6-digit code to a@b.com.")
        assertEquals(TwoFactorFlow.Step.Code, flow.step)
        assertEquals("We sent a 6-digit code to a@b.com.", flow.info)
        // Kept for "Resend code"
        assertEquals("secret12", flow.password)

        flow = flow.withCode("12a34 5678")
        assertEquals("123456", flow.code)
        assertTrue(flow.canConfirm)

        flow = flow.submitting().failed("The code is invalid or has expired.")
        assertEquals("The code is invalid or has expired.", flow.error)
        assertEquals("secret12", flow.password)
        assertEquals(TwoFactorFlow.Step.Code, flow.step)
    }

    @Test
    fun twoFactor_wrongPasswordIsCleared() {
        val flow = TwoFactorFlow(enable = false).withPassword("wrong123").submitting().failed("Incorrect password.")
        assertEquals("", flow.password)
        assertEquals(TwoFactorFlow.Step.Password, flow.step)
        assertEquals("Incorrect password.", flow.error)
    }
}
