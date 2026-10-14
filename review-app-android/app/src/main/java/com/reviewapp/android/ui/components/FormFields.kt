package com.reviewapp.android.ui.components

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowDropDown
import androidx.compose.material.icons.outlined.Group
import androidx.compose.material.icons.outlined.Lock
import androidx.compose.material.icons.outlined.Public
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Icon
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.style.TextAlign
import com.reviewapp.android.domain.model.Visibility

/** Text field with a live "n / max" counter that turns red over the limit (like the web forms). */
@Composable
fun LimitedTextField(
    value: String,
    onValueChange: (String) -> Unit,
    label: String,
    maxLength: Int,
    modifier: Modifier = Modifier,
    minLines: Int = 1,
    singleLine: Boolean = false,
    placeholder: String? = null,
) {
    val tooLong = value.length > maxLength
    OutlinedTextField(
        value = value,
        onValueChange = onValueChange,
        label = { Text(label) },
        placeholder = placeholder?.let { { Text(it) } },
        minLines = minLines,
        singleLine = singleLine,
        isError = tooLong,
        supportingText = {
            Text(
                "${value.length} / $maxLength",
                modifier = Modifier.fillMaxWidth(),
                textAlign = TextAlign.End,
            )
        },
        keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.Sentences),
        modifier = modifier.fillMaxWidth(),
    )
}

@Composable
fun VisibilityField(selected: Visibility, onSelect: (Visibility) -> Unit, modifier: Modifier = Modifier) {
    var expanded by remember { mutableStateOf(false) }
    Box(modifier) {
        OutlinedTextField(
            value = selected.label,
            onValueChange = {},
            readOnly = true,
            label = { Text("Visibility") },
            leadingIcon = { Icon(selected.icon, null) },
            trailingIcon = { Icon(Icons.Filled.ArrowDropDown, null) },
            modifier = Modifier.fillMaxWidth(),
        )
        // A read-only text field still takes the touch, so a transparent layer opens the menu
        Box(Modifier.matchParentSize().clickable { expanded = true })
        DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
            Visibility.entries.forEach { visibility ->
                DropdownMenuItem(
                    text = { Text(visibility.label) },
                    leadingIcon = { Icon(visibility.icon, null) },
                    onClick = {
                        expanded = false
                        onSelect(visibility)
                    },
                )
            }
        }
    }
}

/** Same icons as the web app's visibility chips. */
val Visibility.icon: ImageVector
    get() = when (this) {
        Visibility.Public -> Icons.Outlined.Public
        Visibility.FollowersOnly -> Icons.Outlined.Group
        Visibility.Private -> Icons.Outlined.Lock
    }
