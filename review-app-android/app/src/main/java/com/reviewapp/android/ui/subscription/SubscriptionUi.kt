package com.reviewapp.android.ui.subscription

import com.reviewapp.android.domain.model.Plan
import com.reviewapp.android.domain.model.SubscriptionStatus
import com.reviewapp.android.domain.model.SubscriptionSummary
import java.text.NumberFormat
import java.util.Currency
import java.util.Locale

// Pure presentation rules of the subscription screens (unit-tested), mirroring the web's SubscriptionPanel.

/** What `reviewapp://subscription/{result}` reports; anything unknown counts as cancelled. */
enum class CheckoutReturn {
    Success, Cancelled, Portal;

    companion object {
        fun fromResult(result: String?): CheckoutReturn = when (result) {
            "success" -> Success
            "portal" -> Portal
            else -> Cancelled
        }
    }
}

/** The subscription card: chips, the date line and which actions are offered. */
data class SubscriptionView(
    val chips: List<Chip>,
    /** e.g. "Next renewal" / "Premium until"; null when there is no date to show. */
    val dateLabel: String?,
    val note: String,
    /** A renewal failed: shown as a warning. */
    val warning: String?,
    val canSubscribe: Boolean,
    val canResume: Boolean,
    val canCancel: Boolean,
    val canManagePayment: Boolean,
) {
    enum class Chip(val label: String) { Renews("Renews automatically"), Cancelled("Cancelled"), PaymentProblem("Payment problem") }
}

fun subscriptionView(summary: SubscriptionSummary): SubscriptionView {
    if (!summary.isPremium) {
        return SubscriptionView(
            chips = emptyList(),
            dateLabel = null,
            note = if (summary.status == SubscriptionStatus.Ended) {
                "Your Premium subscription has ended. You can subscribe again any time."
            } else {
                "You're on the Free plan."
            },
            warning = null,
            canSubscribe = true,
            canResume = false,
            canCancel = false,
            // Invoices of an earlier subscription stay reachable
            canManagePayment = summary.status == SubscriptionStatus.Ended,
        )
    }

    val chips = buildList {
        if (summary.willRenew) add(SubscriptionView.Chip.Renews)
        if (summary.cancelAtPeriodEnd) add(SubscriptionView.Chip.Cancelled)
        if (summary.hasPaymentProblem) add(SubscriptionView.Chip.PaymentProblem)
    }
    return SubscriptionView(
        chips = chips,
        dateLabel = when {
            summary.hasPaymentProblem -> "Update your payment details by"
            summary.cancelAtPeriodEnd -> "Premium until"
            else -> "Next renewal"
        },
        note = when {
            summary.hasPaymentProblem -> "If the payment still fails by then, your account switches back to Free."
            summary.cancelAtPeriodEnd -> "Your subscription won't renew. You can resume it until then."
            else -> "You can cancel any time."
        },
        warning = if (summary.hasPaymentProblem) "We couldn't renew your subscription." else null,
        canSubscribe = false,
        canResume = summary.cancelAtPeriodEnd,
        canCancel = !summary.cancelAtPeriodEnd,
        canManagePayment = true,
    )
}

/** e.g. "€4.99 / month" (the symbol and separator follow [locale]). */
fun formatPrice(plan: Plan, locale: Locale = Locale.getDefault()): String {
    val format = NumberFormat.getCurrencyInstance(locale)
    runCatching { format.currency = Currency.getInstance(plan.currency) }
    return "${format.format(plan.priceAmount)} / ${plan.interval}"
}
