package com.reviewapp.android.data.local

import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.booleanPreferencesKey
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map

enum class ThemeMode(val label: String) { System("System"), Light("Light"), Dark("Dark") }

/** Non-sensitive user preferences (the web app keeps the same in localStorage). */
class SettingsStore(private val dataStore: DataStore<Preferences>) {

    val themeMode: Flow<ThemeMode> = dataStore.data.map { prefs ->
        prefs[THEME_KEY]?.let { name -> ThemeMode.entries.firstOrNull { it.name == name } } ?: ThemeMode.System
    }

    suspend fun setThemeMode(mode: ThemeMode) {
        dataStore.edit { it[THEME_KEY] = mode.name }
    }

    /** The camera recognition screen's "your photo goes to an AI service" notice was dismissed. */
    val recognitionNoticeDismissed: Flow<Boolean> = dataStore.data.map { it[RECOGNITION_NOTICE_KEY] ?: false }

    suspend fun dismissRecognitionNotice() {
        dataStore.edit { it[RECOGNITION_NOTICE_KEY] = true }
    }

    private companion object {
        val THEME_KEY = stringPreferencesKey("theme_mode")
        val RECOGNITION_NOTICE_KEY = booleanPreferencesKey("recognition_notice_dismissed")
    }
}
