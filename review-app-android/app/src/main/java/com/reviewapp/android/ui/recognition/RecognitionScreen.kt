package com.reviewapp.android.ui.recognition

import android.Manifest
import android.app.Activity
import android.content.Context
import android.content.ContextWrapper
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.provider.Settings
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.PickVisualMediaRequest
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.PhotoCamera
import androidx.compose.material.icons.filled.PhotoLibrary
import androidx.compose.material.icons.outlined.Info
import androidx.compose.material.icons.outlined.WarningAmber
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.core.app.ActivityCompat
import androidx.core.content.ContextCompat
import androidx.lifecycle.compose.LifecycleResumeEffect
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import coil.compose.AsyncImage
import com.reviewapp.android.domain.model.Media
import com.reviewapp.android.domain.model.RecognitionAllowance
import com.reviewapp.android.domain.model.RecognitionOutcome
import com.reviewapp.android.domain.model.ScoreStats
import com.reviewapp.android.ui.components.BackButton
import com.reviewapp.android.ui.components.ErrorState
import com.reviewapp.android.ui.components.LoadingState
import com.reviewapp.android.ui.components.MediaCard
import com.reviewapp.android.ui.components.PremiumUpsell
import com.reviewapp.android.ui.theme.AppTheme
import java.io.File

/**
 * Camera recognition (Premium): take or pick a photo of something movie, series or music related and get related
 * media, closest first. Free users see the upsell; the back button steps back through the flow first.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun RecognitionScreen(
    onBack: () -> Unit,
    onLogin: () -> Unit,
    onOpenMedia: (Media) -> Unit,
    onOpenPremium: () -> Unit,
    viewModel: RecognitionViewModel = viewModel(factory = RecognitionViewModel.Factory),
) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    val stats by viewModel.scoreStats.collectAsStateWithLifecycle()

    BackHandler(enabled = state.step != RecognitionStep.Start) { viewModel.back() }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Camera recognition") },
                navigationIcon = { BackButton { if (!viewModel.back()) onBack() } },
            )
        },
    ) { padding ->
        val modifier = Modifier.fillMaxSize().padding(padding)
        val allowance = state.allowance
        when {
            !state.isLoggedIn -> Column(modifier.padding(24.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                Text("Log in to use camera recognition.", style = MaterialTheme.typography.bodyLarge, textAlign = TextAlign.Center)
                Button(onClick = onLogin, modifier = Modifier.padding(top = 16.dp)) { Text("Log in") }
            }
            state.allowanceError != null -> Column(modifier) { ErrorState(state.allowanceError!!, viewModel::loadAllowance, Modifier.fillMaxWidth()) }
            allowance == null -> Column(modifier) { LoadingState() }
            !allowance.enabled -> Column(modifier.padding(24.dp)) {
                Intro()
                InfoText("Camera recognition is temporarily unavailable. Please check back later.")
            }
            !allowance.premium -> Column(modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
                Intro()
                PremiumUpsell(
                    onSeePremium = onOpenPremium,
                    title = "Camera recognition is a Premium feature",
                    message = "Subscribe to Premium to recognise figures, posters, covers and collectibles with your camera.",
                )
            }
            else -> when (val step = state.step) {
                RecognitionStep.Start -> StartStep(state, allowance, viewModel, modifier)
                RecognitionStep.Camera -> CameraStep(viewModel, modifier)
                is RecognitionStep.Preview -> PhotoStep(step.photoPath, recognizing = false, state, viewModel, modifier)
                is RecognitionStep.Recognizing -> PhotoStep(step.photoPath, recognizing = true, state, viewModel, modifier)
                RecognitionStep.Results -> ResultsStep(state, stats, viewModel, onOpenMedia, modifier)
            }
        }
    }
}

@Composable
private fun Intro() {
    Column(Modifier.fillMaxWidth(), horizontalAlignment = Alignment.CenterHorizontally) {
        Icon(Icons.Filled.PhotoCamera, contentDescription = null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(48.dp))
        Text(
            "Take a photo of a figure, poster, album cover or other collectible, and find the movies, series and music it relates to.",
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            textAlign = TextAlign.Center,
        )
    }
}

@Composable
private fun StartStep(state: RecognitionUiState, allowance: RecognitionAllowance, viewModel: RecognitionViewModel, modifier: Modifier) {
    val context = LocalContext.current
    val hasCameraHardware = remember { context.packageManager.hasSystemFeature(PackageManager.FEATURE_CAMERA_ANY) }
    var permissionMessage by rememberSaveable { mutableStateOf<String?>(null) }
    var permanentlyDenied by rememberSaveable { mutableStateOf(false) }
    var showRationale by remember { mutableStateOf(false) }

    val permissionLauncher = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        if (granted) {
            permissionMessage = null
            viewModel.openCamera()
        } else {
            // After a denial the system only asks again while a rationale may be shown; otherwise it's "don't ask again"
            val canAskAgain = context.findActivity()?.let {
                ActivityCompat.shouldShowRequestPermissionRationale(it, Manifest.permission.CAMERA)
            } ?: false
            permanentlyDenied = !canAskAgain
            permissionMessage = if (canAskAgain) "Camera access was denied. You can still choose a photo."
            else "Camera access is turned off for this app. Allow it in the settings, or choose a photo instead."
        }
    }
    val pickPhoto = rememberLauncherForActivityResult(ActivityResultContracts.PickVisualMedia()) { uri ->
        uri?.let(viewModel::onPhotoPicked)
    }

    // Coming back from the system settings with the permission granted
    LifecycleResumeEffect(Unit) {
        if (permanentlyDenied && context.hasCameraPermission()) {
            permanentlyDenied = false
            permissionMessage = null
        }
        onPauseOrDispose { }
    }
    LaunchedEffect(hasCameraHardware) {
        if (!hasCameraHardware) viewModel.onCameraUnavailable("This device has no camera. Choose a photo instead.")
    }

    fun startCamera() {
        val activity = context.findActivity()
        when {
            context.hasCameraPermission() -> viewModel.openCamera()
            activity != null && ActivityCompat.shouldShowRequestPermissionRationale(activity, Manifest.permission.CAMERA) -> showRationale = true
            else -> permissionLauncher.launch(Manifest.permission.CAMERA)
        }
    }

    LazyColumn(modifier, contentPadding = PaddingValues(16.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
        item { Intro() }
        item { AllowanceChip(allowance) }
        if (!state.noticeDismissed) {
            item {
                Card(colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.secondaryContainer)) {
                    Column(Modifier.padding(16.dp)) {
                        Row {
                            Icon(Icons.Outlined.Info, contentDescription = null, modifier = Modifier.padding(end = 12.dp))
                            Text(
                                "Your photo is sent to an external AI service (Google Gemini) for analysis. It is not stored, and it is only used for this recognition.",
                                style = MaterialTheme.typography.bodyMedium,
                            )
                        }
                        TextButton(onClick = viewModel::dismissNotice, modifier = Modifier.align(Alignment.End)) { Text("Got it") }
                    }
                }
            }
        }
        state.error?.let { item { ErrorText(it) } }

        if (state.outOfRecognitions) {
            item {
                val time = formatResetTime(allowance.resetsAt)
                InfoText("You have used all your recognitions for today." + (time?.let { " They reset at $it." } ?: ""))
            }
        } else {
            (state.cameraMessage ?: permissionMessage)?.let { message ->
                item {
                    Column {
                        InfoText(message)
                        if (permanentlyDenied && state.cameraMessage == null) {
                            TextButton(onClick = { context.openAppSettings() }) { Text("Open settings") }
                        }
                    }
                }
            }
            item {
                Column(Modifier.fillMaxWidth(), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(12.dp)) {
                    if (state.isPreparing) {
                        CircularProgressIndicator()
                    } else {
                        if (state.cameraMessage == null && !permanentlyDenied) {
                            Button(onClick = ::startCamera, modifier = Modifier.fillMaxWidth()) {
                                Icon(Icons.Filled.PhotoCamera, contentDescription = null)
                                Text("Take a photo", Modifier.padding(start = 8.dp))
                            }
                        }
                        OutlinedButton(
                            onClick = { pickPhoto.launch(PickVisualMediaRequest(ActivityResultContracts.PickVisualMedia.ImageOnly)) },
                            modifier = Modifier.fillMaxWidth(),
                        ) {
                            Icon(Icons.Filled.PhotoLibrary, contentDescription = null)
                            Text("Choose a photo", Modifier.padding(start = 8.dp))
                        }
                    }
                }
            }
            item { Tips() }
        }
    }

    if (showRationale) {
        AlertDialog(
            onDismissRequest = { showRationale = false },
            title = { Text("Allow the camera") },
            text = { Text("The camera is used only while you take a photo for recognition. You can also choose a photo instead.") },
            confirmButton = {
                TextButton(onClick = {
                    showRationale = false
                    permissionLauncher.launch(Manifest.permission.CAMERA)
                }) { Text("Continue") }
            },
            dismissButton = { TextButton(onClick = { showRationale = false }) { Text("Not now") } },
        )
    }
}

@Composable
private fun CameraStep(viewModel: RecognitionViewModel, modifier: Modifier) {
    Column(modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        CameraCapture(
            newFile = viewModel::newCaptureFile,
            onCaptured = viewModel::onPhotoCaptured,
            onUnavailable = viewModel::onCameraUnavailable,
            modifier = Modifier.weight(1f),
        )
        TextButton(onClick = { viewModel.back() }, modifier = Modifier.align(Alignment.CenterHorizontally)) { Text("Back") }
    }
}

@Composable
private fun PhotoStep(path: String, recognizing: Boolean, state: RecognitionUiState, viewModel: RecognitionViewModel, modifier: Modifier) {
    Column(modifier.padding(16.dp), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Box(Modifier.fillMaxWidth().weight(1f), contentAlignment = Alignment.Center) {
            AsyncImage(
                model = File(path),
                contentDescription = "Your photo",
                contentScale = ContentScale.Fit,
                alpha = if (recognizing) 0.5f else 1f,
                modifier = Modifier.fillMaxSize(),
            )
            if (recognizing) CircularProgressIndicator()
        }
        Column(Modifier.semantics { liveRegion = LiveRegionMode.Polite }, horizontalAlignment = Alignment.CenterHorizontally) {
            if (recognizing) Text("Recognising your photo...", style = MaterialTheme.typography.bodyLarge)
            state.error?.let { ErrorText(it) }
        }
        if (recognizing) {
            TextButton(onClick = viewModel::cancel) { Text("Cancel") }
        } else {
            Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                Button(onClick = viewModel::usePhoto, enabled = !state.outOfRecognitions) { Text("Use photo") }
                OutlinedButton(onClick = viewModel::retake) { Text("Retake") }
            }
        }
    }
}

@Composable
private fun ResultsStep(
    state: RecognitionUiState,
    stats: Map<String, ScoreStats>,
    viewModel: RecognitionViewModel,
    onOpenMedia: (Media) -> Unit,
    modifier: Modifier,
) {
    val results = state.results ?: return
    LazyColumn(modifier, contentPadding = PaddingValues(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        item { state.allowance?.let { AllowanceChip(it) } }
        item {
            Surface(
                color = if (results.outcome == RecognitionOutcome.Recognised) AppTheme.extendedColors.success.copy(alpha = 0.15f)
                else MaterialTheme.colorScheme.surfaceVariant,
                shape = MaterialTheme.shapes.medium,
                modifier = Modifier.fillMaxWidth(),
            ) {
                Text(results.summary, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(16.dp))
            }
        }
        if (results.incomplete) {
            item { WarningText("Some movie, series or music sources didn't answer, so the list may be incomplete.") }
        }

        if (results.items.isEmpty() && !results.isLoadingMore) {
            item {
                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Text(if (results.expired) "These suggestions have expired. Take a new photo." else emptyResultMessage(results.outcome))
                    if (!results.expired) Tips()
                }
            }
        }

        itemsIndexed(results.items, key = { _, item -> item.media.key }) { index, item ->
            Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                // A heading where the relation gets looser
                if (index == 0 || results.items[index - 1].tier != item.tier) {
                    Text(item.tier.label.uppercase(), style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
                MediaCard(
                    media = item.media,
                    stats = stats[item.media.key],
                    onNeedStats = viewModel::requestStats,
                    onClick = onOpenMedia,
                    badge = item.tier.label,
                )
            }
        }

        item {
            Box(Modifier.fillMaxWidth().heightIn(min = 48.dp), contentAlignment = Alignment.Center) {
                when {
                    results.isLoadingMore -> CircularProgressIndicator(Modifier.size(28.dp))
                    results.moreError != null -> ErrorState(results.moreError, viewModel::retryMore)
                    results.expired && results.items.isNotEmpty() -> Text(
                        "These suggestions have expired. Take a new photo to see more.",
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        textAlign = TextAlign.Center,
                    )
                    results.canShowMore -> OutlinedButton(onClick = viewModel::showMore) { Text("Show more") }
                    results.isEnd -> Text("That's everything we found.", color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
        }
        item {
            Button(onClick = viewModel::startOver, modifier = Modifier.fillMaxWidth()) {
                Icon(Icons.Filled.PhotoCamera, contentDescription = null)
                Text("Try another photo", Modifier.padding(start = 8.dp))
            }
        }
    }
}

@Composable
private fun AllowanceChip(allowance: RecognitionAllowance) {
    val text = allowanceText(allowance.remaining, allowance.limit) ?: return
    Box(Modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
        Surface(
            color = if (allowance.remaining == 0) AppTheme.extendedColors.warning.copy(alpha = 0.2f) else MaterialTheme.colorScheme.surfaceVariant,
            shape = MaterialTheme.shapes.large,
        ) {
            Text(text, style = MaterialTheme.typography.labelLarge, modifier = Modifier.padding(horizontal = 12.dp, vertical = 6.dp))
        }
    }
}

@Composable
private fun Tips() {
    Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
        Text("Tips for a good result", style = MaterialTheme.typography.titleSmall)
        RECOGNITION_TIPS.forEach { Text("• $it", style = MaterialTheme.typography.bodyMedium) }
    }
}

@Composable
private fun InfoText(text: String) {
    Text(text, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(vertical = 8.dp))
}

@Composable
private fun ErrorText(text: String) {
    Text(text, color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodyMedium, textAlign = TextAlign.Center)
}

@Composable
private fun WarningText(text: String) {
    Row(verticalAlignment = Alignment.CenterVertically) {
        Icon(Icons.Outlined.WarningAmber, contentDescription = null, tint = AppTheme.extendedColors.warning)
        Text(text, style = MaterialTheme.typography.bodyMedium, modifier = Modifier.padding(start = 8.dp))
    }
}

private fun Context.hasCameraPermission() =
    ContextCompat.checkSelfPermission(this, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED

private fun Context.findActivity(): Activity? {
    var context = this
    while (context is ContextWrapper) {
        if (context is Activity) return context
        context = context.baseContext
    }
    return null
}

private fun Context.openAppSettings() {
    startActivity(Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.fromParts("package", packageName, null)))
}
