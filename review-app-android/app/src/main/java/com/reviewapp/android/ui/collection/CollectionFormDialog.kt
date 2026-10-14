package com.reviewapp.android.ui.collection

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.size
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.reviewapp.android.domain.model.Visibility
import com.reviewapp.android.ui.components.LimitedTextField
import com.reviewapp.android.ui.components.VisibilityField
import com.reviewapp.android.util.Validation

/** Create / edit a collection: name (1-50 after trimming) and visibility, Private by default like on the web. */
@Composable
fun CollectionFormDialog(
    title: String,
    confirmLabel: String,
    isSaving: Boolean,
    error: String?,
    onConfirm: (name: String, visibility: Visibility) -> Unit,
    onDismiss: () -> Unit,
    initialName: String = "",
    initialVisibility: Visibility = Visibility.Private,
) {
    var name by rememberSaveable { mutableStateOf(initialName) }
    var visibility by rememberSaveable { mutableStateOf(initialVisibility) }

    AlertDialog(
        onDismissRequest = { if (!isSaving) onDismiss() },
        title = { Text(title) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                LimitedTextField(
                    value = name,
                    onValueChange = { name = it },
                    label = "Collection name",
                    maxLength = Validation.MAX_COLLECTION_NAME_LENGTH,
                    singleLine = true,
                )
                VisibilityField(visibility, onSelect = { visibility = it })
                error?.let { Text(it, color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodyMedium) }
            }
        },
        confirmButton = {
            TextButton(
                onClick = { onConfirm(name.trim(), visibility) },
                enabled = !isSaving && Validation.collectionNameError(name) == null,
            ) {
                if (isSaving) CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp) else Text(confirmLabel)
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss, enabled = !isSaving) { Text("Cancel") }
        },
    )
}
