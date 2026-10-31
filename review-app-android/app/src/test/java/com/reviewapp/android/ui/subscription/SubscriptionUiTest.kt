package com.reviewapp.android.ui.subscription

import com.reviewapp.android.domain.model.Plan
import com.reviewapp.android.domain.model.SubscriptionStatus
import com.reviewapp.android.domain.model.SubscriptionSummary
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.util.Locale

class SubscriptionUiTest {

    private fun summary(
        status: SubscriptionStatus = SubscriptionStatus.Active,
        isPremium: Boolean = true,
        willRenew: Boolean = true,
        cancelled: Boolean = false,
        paymentProblem: Boolean = false,
    ) = SubscriptionSummary(status, isPremium, willRenew, cancelled, paymentProblem, "2026-11-25T15:23:21Z")

    @Test
    fun free_offersOnlySubscribe() {
        val view = subscriptionView(summary(SubscriptionStatus.None, isPremium = false, willRenew = false))

        assertTrue(view.canSubscribe)
        assertFalse(view.canCancel)
        assertFalse(view.canResume)
        assertFalse(view.canManagePayment)
        assertNull(view.dateLabel)
        assertTrue(view.chips.isEmpty())
    }

    @Test
    fun ended_canSubscribeAgainAndSeeInvoices() {
        val view = subscriptionView(summary(SubscriptionStatus.Ended, isPremium = false, willRenew = false))

        assertTrue(view.canSubscribe)
        assertTrue(view.canManagePayment)
        assertTrue(view.note.contains("ended"))
    }

    @Test
    fun active_renewsAndCanBeCancelled() {
        val view = subscriptionView(summary())

        assertEquals(listOf(SubscriptionView.Chip.Renews), view.chips)
        assertEquals("Next renewal", view.dateLabel)
        assertTrue(view.canCancel)
        assertFalse(view.canResume)
        assertFalse(view.canSubscribe)
        assertNull(view.warning)
    }

    @Test
    fun cancelled_canBeResumedNotCancelledAgain() {
        val view = subscriptionView(summary(willRenew = false, cancelled = true))

        assertEquals(listOf(SubscriptionView.Chip.Cancelled), view.chips)
        assertEquals("Premium until", view.dateLabel)
        assertTrue(view.canResume)
        assertFalse(view.canCancel)
    }

    @Test
    fun pastDue_warnsAndPointsToPaymentDetails() {
        val view = subscriptionView(summary(SubscriptionStatus.PastDue, willRenew = false, paymentProblem = true))

        assertEquals(listOf(SubscriptionView.Chip.PaymentProblem), view.chips)
        assertEquals("Update your payment details by", view.dateLabel)
        assertTrue(view.warning != null)
        assertTrue(view.canManagePayment)
        assertTrue(view.canCancel)
    }

    @Test
    fun checkoutReturn_unknownCountsAsCancelled() {
        assertEquals(CheckoutReturn.Success, CheckoutReturn.fromResult("success"))
        assertEquals(CheckoutReturn.Portal, CheckoutReturn.fromResult("portal"))
        assertEquals(CheckoutReturn.Cancelled, CheckoutReturn.fromResult("cancelled"))
        assertEquals(CheckoutReturn.Cancelled, CheckoutReturn.fromResult("<script>"))
        assertEquals(CheckoutReturn.Cancelled, CheckoutReturn.fromResult(null))
    }

    @Test
    fun formatPrice_usesCurrencyAndInterval() {
        val plan = Plan(4.99, "EUR", "month", emptyList())

        assertEquals("€4.99 / month", formatPrice(plan, Locale.US))
    }

    @Test
    fun formatPrice_unknownCurrencyStillShowsAmount() {
        val plan = Plan(4.99, "???", "month", emptyList())

        assertTrue(formatPrice(plan, Locale.US).contains("4.99"))
    }
}
