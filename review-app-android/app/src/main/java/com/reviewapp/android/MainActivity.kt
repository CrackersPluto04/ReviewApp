package com.reviewapp.android

import android.graphics.Color
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.SystemBarStyle
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.reviewapp.android.data.local.ThemeMode
import com.reviewapp.android.ui.navigation.ReviewAppRoot
import com.reviewapp.android.ui.theme.ReviewAppTheme

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        val settingsStore = (application as ReviewApplication).container.settingsStore

        setContent {
            val themeMode by settingsStore.themeMode.collectAsStateWithLifecycle(initialValue = ThemeMode.System)
            val darkTheme = when (themeMode) {
                ThemeMode.System -> isSystemInDarkTheme()
                ThemeMode.Light -> false
                ThemeMode.Dark -> true
            }

            // enableEdgeToEdge() follows the system theme, the app's own theme setting can differ
            DisposableEffect(darkTheme) {
                enableEdgeToEdge(
                    statusBarStyle = SystemBarStyle.auto(Color.TRANSPARENT, Color.TRANSPARENT) { darkTheme },
                    navigationBarStyle = SystemBarStyle.auto(LIGHT_SCRIM, DARK_SCRIM) { darkTheme },
                )
                onDispose {}
            }

            ReviewAppTheme(darkTheme = darkTheme) {
                ReviewAppRoot()
            }
        }
    }

    private companion object {
        // The defaults enableEdgeToEdge() uses for the navigation bar scrim
        val LIGHT_SCRIM = Color.argb(0xe6, 0xFF, 0xFF, 0xFF)
        val DARK_SCRIM = Color.argb(0x80, 0x1b, 0x1b, 0x1b)
    }
}
