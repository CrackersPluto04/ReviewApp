package com.reviewapp.android.ui.theme

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.Immutable
import androidx.compose.runtime.ReadOnlyComposable
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.graphics.Color
import com.reviewapp.android.util.ScoreTier

private val LightColorScheme = lightColorScheme(
    primary = Blue700,
    onPrimary = Color.White,
    primaryContainer = Blue50,
    onPrimaryContainer = Blue900,
    secondary = Purple700,
    onSecondary = Color.White,
    secondaryContainer = Purple50,
    onSecondaryContainer = Purple900,
    error = Red700,
)

private val DarkColorScheme = darkColorScheme(
    primary = Blue200,
    onPrimary = Blue900,
    primaryContainer = Blue900,
    onPrimaryContainer = Blue50,
    secondary = Purple200,
    onSecondary = Purple900,
    secondaryContainer = Purple900,
    onSecondaryContainer = Purple50,
    error = Red400,
)

/** Colors Material 3 has no slot for: success (login button, pros), warning, and the pros/cons boxes. */
@Immutable
data class ExtendedColors(
    val success: Color,
    val onSuccess: Color,
    val successContainer: Color,
    val warning: Color,
    val errorContainer: Color,
)

private val LightExtendedColors = ExtendedColors(
    success = Green800,
    onSuccess = Color.White,
    successContainer = Green50,
    warning = Orange700,
    errorContainer = Red50,
)

private val DarkExtendedColors = ExtendedColors(
    success = Green400,
    onSuccess = Green900,
    successContainer = Green900,
    warning = Orange400,
    errorContainer = Red900,
)

private val LocalExtendedColors = staticCompositionLocalOf { LightExtendedColors }

/** Access point for the theme values that [MaterialTheme] does not carry. */
object AppTheme {
    /** The [ExtendedColors] of the current (light or dark) theme. */
    val extendedColors: ExtendedColors
        @Composable @ReadOnlyComposable get() = LocalExtendedColors.current
}

/** Green / orange / red like the web app's score colours. */
@Composable
@ReadOnlyComposable
fun ScoreTier.color(): Color = when (this) {
    ScoreTier.Great -> AppTheme.extendedColors.success
    ScoreTier.Mixed -> AppTheme.extendedColors.warning
    ScoreTier.Poor -> MaterialTheme.colorScheme.error
}

/**
 * The app's theme: the fixed palette (no dynamic colour, so the app matches the web app) in its light or dark
 * variant, the typography and the [ExtendedColors]. [darkTheme] is resolved by the caller from the user's theme
 * setting, so "system" has already been mapped to a boolean.
 */
@Composable
fun ReviewAppTheme(
    darkTheme: Boolean,
    content: @Composable () -> Unit,
) {
    CompositionLocalProvider(LocalExtendedColors provides if (darkTheme) DarkExtendedColors else LightExtendedColors) {
        MaterialTheme(
            colorScheme = if (darkTheme) DarkColorScheme else LightColorScheme,
            typography = Typography,
            content = content,
        )
    }
}
