package com.reviewapp.android.ui.components

import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowDropDown
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FilterChipDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import com.reviewapp.android.util.Option

/**
 * Filter chip that opens a menu of [options]. The chip shows the selected option's label, or
 * [label] when [selected] is null (then [allLabel], if given, is offered to clear it).
 */
@Composable
fun DropdownFilterChip(
    label: String,
    options: List<Option>,
    selected: String?,
    onSelect: (String?) -> Unit,
    allLabel: String? = null,
    highlighted: Boolean = selected != null,
) {
    var expanded by remember { mutableStateOf(false) }
    Box {
        FilterChip(
            selected = highlighted,
            onClick = { expanded = true },
            label = { Text(options.firstOrNull { it.value == selected }?.label ?: label) },
            trailingIcon = { Icon(Icons.Filled.ArrowDropDown, null, Modifier.size(FilterChipDefaults.IconSize)) },
        )
        DropdownMenu(
            expanded = expanded,
            onDismissRequest = { expanded = false },
            modifier = Modifier.heightIn(max = 400.dp),
        ) {
            if (allLabel != null) {
                MenuOption(allLabel, selected == null) { expanded = false; onSelect(null) }
            }
            options.forEach { option ->
                MenuOption(option.label, option.value == selected) { expanded = false; onSelect(option.value) }
            }
        }
    }
}

@Composable
private fun MenuOption(text: String, isSelected: Boolean, onClick: () -> Unit) {
    DropdownMenuItem(
        text = { Text(text) },
        onClick = onClick,
        trailingIcon = if (isSelected) ({ Icon(Icons.Filled.Check, contentDescription = "Selected") }) else null,
    )
}

/** Dialog with one or more whole-number fields (year, runtime). Blank fields are null. */
@Composable
fun NumberFieldsDialog(
    title: String,
    fields: List<Pair<String, Int?>>,
    validate: (List<Int?>) -> String?,
    onConfirm: (List<Int?>) -> Unit,
    onDismiss: () -> Unit,
) {
    var values by rememberSaveable { mutableStateOf(fields.map { it.second?.toString().orEmpty() }) }
    val parsed = values.map { it.toIntOrNull() }
    val error = validate(parsed)

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(title) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                fields.forEachIndexed { index, (fieldLabel, _) ->
                    OutlinedTextField(
                        value = values[index],
                        onValueChange = { input ->
                            if (input.length <= 4 && input.all(Char::isDigit)) {
                                values = values.toMutableList().also { it[index] = input }
                            }
                        },
                        label = { Text(fieldLabel) },
                        singleLine = true,
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                        modifier = Modifier.fillMaxWidth(),
                    )
                }
                if (error != null) Text(error, color = MaterialTheme.colorScheme.error)
            }
        },
        confirmButton = {
            TextButton(onClick = { onConfirm(parsed) }, enabled = error == null) { Text("Apply") }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Cancel") } },
    )
}

/** Horizontally scrollable row of filter chips. */
@Composable
fun FilterChipRow(modifier: Modifier = Modifier, content: @Composable () -> Unit) {
    Row(
        modifier = modifier.horizontalScroll(rememberScrollState()),
        horizontalArrangement = Arrangement.spacedBy(8.dp),
    ) { content() }
}

/** Search text field with a search button, submits on the keyboard's search action too. */
@Composable
fun SearchField(
    value: String,
    onValueChange: (String) -> Unit,
    onSearch: () -> Unit,
    modifier: Modifier = Modifier,
    label: String = "Search movies, series, music",
) {
    OutlinedTextField(
        value = value,
        onValueChange = onValueChange,
        label = { Text(label) },
        singleLine = true,
        keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
        keyboardActions = KeyboardActions(onSearch = { onSearch() }),
        trailingIcon = {
            IconButton(onClick = onSearch, enabled = value.isNotBlank()) {
                Icon(Icons.Filled.Search, contentDescription = "Search")
            }
        },
        shape = RoundedCornerShape(28.dp),
        modifier = modifier.fillMaxWidth(),
    )
}
