package com.reviewapp.android.ui.subscription

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.WorkspacePremium
import androidx.compose.material.icons.outlined.CheckCircle
import androidx.compose.material.icons.outlined.Schedule
import androidx.compose.material.icons.outlined.WarningAmber
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
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
import androidx.compose.ui.platform.LocalUriHandler
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.LifecycleResumeEffect
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import com.reviewapp.android.domain.model.Plan
import com.reviewapp.android.domain.model.SubscriptionSummary
import com.reviewapp.android.ui.components.BackButton
import com.reviewapp.android.ui.components.ErrorState
import com.reviewapp.android.ui.components.LoadingState
import com.reviewapp.android.ui.components.PremiumBadge
import com.reviewapp.android.ui.components.PremiumGold
import com.reviewapp.android.ui.theme.AppTheme
import com.reviewapp.android.util.formatDate

private val FreeFeatures = listOf(
    "Reviews, replies and scores",
    "Collections and favourites",
    "Followers and achievements",
    "Everything the app offers today stays free",
)

/** Free vs Premium, subscribing, and the current subscription for subscribers. Visible without logging in. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun PremiumScreen(
    onBack: () -> Unit,
    onLogin: () -> Unit,
    viewModel: PremiumViewModel = viewModel(factory = PremiumViewModel.Factory),
) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    val snackbarHostState = remember { SnackbarHostState() }
    val uriHandler = LocalUriHandler.current
    val currentOnLogin by rememberUpdatedState(onLogin)

    LaunchedEffect(viewModel) {
        viewModel.events.collect { event ->
            when (event) {
                // Payment happens on the provider's page in the browser, never in the app
                is PremiumEvent.OpenUrl -> runCatching { uriHandler.openUri(event.url) }
                    .onFailure { snackbarHostState.showSnackbar("No browser found to open the payment page.") }
                is PremiumEvent.Message -> snackbarHostState.showSnackbar(event.text)
                PremiumEvent.LoginRequired -> currentOnLogin()
            }
        }
    }
    LifecycleResumeEffect(viewModel) {
        viewModel.onResume()
        onPauseOrDispose { }
    }

    Scaffold(
        topBar = { TopAppBar(title = { Text("Premium") }, navigationIcon = { BackButton(onBack) }) },
        snackbarHost = { SnackbarHost(snackbarHostState) },
    ) { padding ->
        val modifier = Modifier.fillMaxSize().padding(padding)
        val plan = state.plan
        when {
            plan == null && state.isLoading -> Column(modifier) { LoadingState() }
            plan == null -> Column(modifier) {
                ErrorState(state.error ?: "Could not load the plans.", viewModel::loadPlan, Modifier.fillMaxWidth())
            }
            else -> PremiumContent(state, plan, viewModel, modifier)
        }
    }

    val subscription = state.subscription
    if (state.confirmCancel && subscription != null) {
        CancelSubscriptionDialog(
            accessEndsOn = formatDate(subscription.accessEndsAt),
            isBusy = state.isBusy,
            onConfirm = viewModel::confirmCancel,
            onDismiss = viewModel::dismissCancel,
        )
    }
}

@Composable
private fun PremiumContent(state: PremiumUiState, plan: Plan, viewModel: PremiumViewModel, modifier: Modifier) {
    val subscription = state.subscription
    val isPremium = subscription?.isPremium == true

    LazyColumn(
        modifier,
        contentPadding = PaddingValues(16.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp),
    ) {
        item {
            Column(Modifier.fillMaxWidth(), horizontalAlignment = Alignment.CenterHorizontally) {
                Icon(Icons.Filled.WorkspacePremium, contentDescription = null, tint = PremiumGold, modifier = Modifier.size(56.dp))
                Text("THE Review App Premium", style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Bold, textAlign = TextAlign.Center)
                Text(
                    "Support the app and unlock exclusive features. Cancel any time.",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    textAlign = TextAlign.Center,
                )
            }
        }

        if (subscription != null && (isPremium || subscriptionView(subscription).canManagePayment)) {
            item { SubscriptionCard(subscription, state.isBusy, viewModel) }
        }

        item { PremiumPlanCard(plan, state, isPremium, viewModel::subscribe) }
        item { FreePlanCard(isCurrent = state.isLoggedIn && !isPremium) }

        if (state.isLoggedIn) {
            item {
                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Text("Early access", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)
                    Text(
                        "Premium members can try new features before anyone else.",
                        style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                    OutlinedButton(onClick = viewModel::tryPreview) { Text("Open the early access preview") }
                }
            }
        }

        item {
            Text(
                "Terms of Service: the full terms are not published yet. Premium is currently in test mode, no real payments are taken.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}

@Composable
private fun SubscriptionCard(subscription: SubscriptionSummary, isBusy: Boolean, viewModel: PremiumViewModel) {
    val view = subscriptionView(subscription)
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Text("Your subscription", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                if (subscription.isPremium) PremiumBadge()
                view.chips.forEach { StatusChip(it) }
            }
            view.warning?.let { warning ->
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Icon(Icons.Outlined.WarningAmber, contentDescription = null, tint = AppTheme.extendedColors.warning)
                    Text(warning, style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(start = 8.dp))
                }
            }
            val date = formatDate(subscription.accessEndsAt)
            if (view.dateLabel != null && date != null) {
                Text("${view.dateLabel}: $date", style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.SemiBold)
            }
            Text(view.note, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)

            if (view.canResume) {
                Button(onClick = viewModel::resume, enabled = !isBusy, modifier = Modifier.fillMaxWidth()) { Text("Resume subscription") }
            }
            if (view.canManagePayment) {
                OutlinedButton(onClick = viewModel::managePayment, enabled = !isBusy, modifier = Modifier.fillMaxWidth()) {
                    Text("Manage payment details")
                }
            }
            if (view.canCancel) {
                TextButton(
                    onClick = viewModel::askCancel,
                    enabled = !isBusy,
                    colors = ButtonDefaults.textButtonColors(contentColor = MaterialTheme.colorScheme.error),
                    modifier = Modifier.fillMaxWidth(),
                ) { Text("Cancel subscription") }
            }
        }
    }
}

@Composable
private fun StatusChip(chip: SubscriptionView.Chip) {
    val color = when (chip) {
        SubscriptionView.Chip.Renews -> AppTheme.extendedColors.success
        SubscriptionView.Chip.Cancelled -> MaterialTheme.colorScheme.onSurfaceVariant
        SubscriptionView.Chip.PaymentProblem -> AppTheme.extendedColors.warning
    }
    Surface(color = color.copy(alpha = 0.15f), contentColor = color, shape = MaterialTheme.shapes.small) {
        Text(chip.label, style = MaterialTheme.typography.labelMedium, fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(horizontal = 8.dp, vertical = 4.dp))
    }
}

@Composable
private fun PremiumPlanCard(plan: Plan, state: PremiumUiState, isPremium: Boolean, onSubscribe: () -> Unit) {
    val price = formatPrice(plan)
    Card(
        Modifier.fillMaxWidth(),
        border = BorderStroke(2.dp, PremiumGold),
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface),
    ) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text("Premium", style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold)
                Icon(Icons.Filled.WorkspacePremium, contentDescription = null, tint = PremiumGold, modifier = Modifier.padding(start = 6.dp))
            }
            Text(price, style = MaterialTheme.typography.titleMedium)

            plan.features.forEach { feature ->
                FeatureRow(
                    title = feature.title,
                    description = feature.description,
                    comingSoon = feature.comingSoon,
                    available = !feature.comingSoon,
                )
            }

            if (isPremium) {
                Text("Your current plan", style = MaterialTheme.typography.labelLarge, color = PremiumGold, fontWeight = FontWeight.Bold)
            } else {
                Button(onClick = onSubscribe, enabled = !state.isBusy, modifier = Modifier.fillMaxWidth()) {
                    Text(if (state.isLoggedIn) "Subscribe for $price" else "Log in to subscribe")
                }
            }
            Text(
                "Premium renews automatically every ${plan.interval} until you cancel. You can cancel any time and keep Premium " +
                    "until the end of the period you paid for. Payment happens on our payment provider's secure page.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}

@Composable
private fun FreePlanCard(isCurrent: Boolean) {
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Text("Free", style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold)
            Text("Forever", style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
            FreeFeatures.forEach { FeatureRow(title = it, description = null, comingSoon = false, available = true, free = true) }
            if (isCurrent) Text("Your current plan", style = MaterialTheme.typography.labelLarge, fontWeight = FontWeight.Bold)
        }
    }
}

@Composable
private fun FeatureRow(title: String, description: String?, comingSoon: Boolean, available: Boolean, free: Boolean = false) {
    Row(verticalAlignment = Alignment.Top) {
        Icon(
            if (available) Icons.Outlined.CheckCircle else Icons.Outlined.Schedule,
            contentDescription = null,
            tint = when {
                !available -> MaterialTheme.colorScheme.outline
                free -> AppTheme.extendedColors.success
                else -> PremiumGold
            },
            modifier = Modifier.padding(top = 2.dp, end = 12.dp).size(20.dp),
        )
        Column {
            Text(if (comingSoon) "$title (coming soon)" else title, style = MaterialTheme.typography.bodyLarge)
            description?.let {
                Text(it, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        }
    }
}

@Composable
private fun CancelSubscriptionDialog(accessEndsOn: String?, isBusy: Boolean, onConfirm: () -> Unit, onDismiss: () -> Unit) {
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Cancel Premium?") },
        text = {
            Text(
                "Your subscription won't renew. You keep every Premium feature " +
                    (accessEndsOn?.let { "until $it" } ?: "until the end of the period you paid for") +
                    ", and you can resume it until then. Nothing you created is deleted.",
            )
        },
        confirmButton = {
            TextButton(
                onClick = onConfirm,
                enabled = !isBusy,
                colors = ButtonDefaults.textButtonColors(contentColor = MaterialTheme.colorScheme.error),
            ) { Text("Cancel subscription") }
        },
        dismissButton = { TextButton(onClick = onDismiss, enabled = !isBusy) { Text("Keep Premium") } },
    )
}
