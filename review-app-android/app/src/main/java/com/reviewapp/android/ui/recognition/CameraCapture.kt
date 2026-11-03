package com.reviewapp.android.ui.recognition

import androidx.camera.core.CameraSelector
import androidx.camera.core.ImageCapture
import androidx.camera.core.ImageCaptureException
import androidx.camera.view.CameraController
import androidx.camera.view.LifecycleCameraController
import androidx.camera.view.PreviewView
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Cameraswitch
import androidx.compose.material.icons.filled.PhotoCamera
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.content.ContextCompat
import androidx.lifecycle.compose.LocalLifecycleOwner
import java.io.File

/**
 * The live camera picture with a "Take photo" button (CameraX). The camera is only bound while this is on screen
 * and follows the screen's lifecycle, so it is off whenever the app is in the background.
 * [newFile] gives the cache file a photo is written to, [onCaptured] gets it.
 */
@Composable
fun CameraCapture(
    newFile: () -> File,
    onCaptured: (File) -> Unit,
    onUnavailable: (String) -> Unit,
    modifier: Modifier = Modifier,
) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current
    val currentOnUnavailable by rememberUpdatedState(onUnavailable)
    val currentOnCaptured by rememberUpdatedState(onCaptured)

    val controller = remember { LifecycleCameraController(context).apply { setEnabledUseCases(CameraController.IMAGE_CAPTURE) } }
    var ready by remember { mutableStateOf(false) }
    var canSwitch by remember { mutableStateOf(false) }
    var capturing by remember { mutableStateOf(false) }
    var error by remember { mutableStateOf<String?>(null) }

    DisposableEffect(lifecycleOwner, controller) {
        controller.bindToLifecycle(lifecycleOwner)
        val initialization = controller.initializationFuture
        initialization.addListener({
            try {
                initialization.get()
                val back = controller.hasCamera(CameraSelector.DEFAULT_BACK_CAMERA)
                val front = controller.hasCamera(CameraSelector.DEFAULT_FRONT_CAMERA)
                when {
                    !back && !front -> currentOnUnavailable("No camera was found. Choose a photo instead.")
                    else -> {
                        // Devices (and emulators) with only a front camera
                        if (!back) controller.cameraSelector = CameraSelector.DEFAULT_FRONT_CAMERA
                        canSwitch = back && front
                        ready = true
                    }
                }
            } catch (_: Exception) {
                currentOnUnavailable("The camera could not be started. Choose a photo instead.")
            }
        }, ContextCompat.getMainExecutor(context))
        onDispose { controller.unbind() }
    }

    fun takePhoto() {
        capturing = true
        error = null
        val file = newFile()
        controller.takePicture(
            ImageCapture.OutputFileOptions.Builder(file).build(),
            ContextCompat.getMainExecutor(context),
            object : ImageCapture.OnImageSavedCallback {
                override fun onImageSaved(output: ImageCapture.OutputFileResults) {
                    capturing = false
                    currentOnCaptured(file)
                }

                override fun onError(exception: ImageCaptureException) {
                    capturing = false
                    file.delete()
                    error = "The photo could not be taken. Please try again."
                }
            },
        )
    }

    Column(modifier, horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Box(
            Modifier
                .fillMaxWidth()
                .weight(1f, fill = true)
                .background(Color.Black, MaterialTheme.shapes.medium),
            contentAlignment = Alignment.Center,
        ) {
            AndroidView(
                factory = { viewContext ->
                    PreviewView(viewContext).apply {
                        scaleType = PreviewView.ScaleType.FIT_CENTER
                        this.controller = controller
                    }
                },
                modifier = Modifier.matchParentSize().semantics { contentDescription = "Camera preview" },
            )
            if (!ready) CircularProgressIndicator()
        }

        error?.let { Text(it, color = MaterialTheme.colorScheme.error) }

        Row(horizontalArrangement = Arrangement.spacedBy(12.dp), verticalAlignment = Alignment.CenterVertically) {
            Button(onClick = ::takePhoto, enabled = ready && !capturing) {
                if (capturing) CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp)
                else Icon(Icons.Filled.PhotoCamera, contentDescription = null)
                Text("Take photo", Modifier.padding(start = 8.dp))
            }
            if (canSwitch) {
                OutlinedButton(
                    onClick = {
                        controller.cameraSelector =
                            if (controller.cameraSelector == CameraSelector.DEFAULT_BACK_CAMERA) CameraSelector.DEFAULT_FRONT_CAMERA
                            else CameraSelector.DEFAULT_BACK_CAMERA
                    },
                    enabled = !capturing,
                ) {
                    Icon(Icons.Filled.Cameraswitch, contentDescription = null)
                    Text("Switch", Modifier.padding(start = 8.dp))
                }
            }
        }
    }
}
