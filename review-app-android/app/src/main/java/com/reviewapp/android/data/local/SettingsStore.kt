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

    /**
     * The user's offline mode switch (per account, on by default: a Premium user gets it without looking for it).
     * Only used while the user has the `offline-mode` entitlement.
     */
    fun offlineEnabled(userId: Int): Flow<Boolean> = dataStore.data.map { it[offlineKey(userId)] ?: true }

    suspend fun setOfflineEnabled(userId: Int, enabled: Boolean) {
        dataStore.edit { it[offlineKey(userId)] = enabled }
    }

    /** The storage budget of the non-essential offline cache (opened media, lists); own content doesn't count. */
    val cacheBudget: Flow<CacheBudget> = dataStore.data.map { prefs ->
        prefs[CACHE_BUDGET_KEY]?.let { name -> CacheBudget.entries.firstOrNull { it.name == name } } ?: CacheBudget.Default
    }

    suspend fun setCacheBudget(budget: CacheBudget) {
        dataStore.edit { it[CACHE_BUDGET_KEY] = budget.name }
    }

    /** "Make available offline" downloads only on unmetered networks. */
    val offlineDownloadWifiOnly: Flow<Boolean> = dataStore.data.map { it[WIFI_ONLY_KEY] ?: true }

    suspend fun setOfflineDownloadWifiOnly(wifiOnly: Boolean) {
        dataStore.edit { it[WIFI_ONLY_KEY] = wifiOnly }
    }

    private fun offlineKey(userId: Int) = booleanPreferencesKey("offline_enabled_$userId")

    private companion object {
        val THEME_KEY = stringPreferencesKey("theme_mode")
        val RECOGNITION_NOTICE_KEY = booleanPreferencesKey("recognition_notice_dismissed")
        val CACHE_BUDGET_KEY = stringPreferencesKey("offline_cache_budget")
        val WIFI_ONLY_KEY = booleanPreferencesKey("offline_download_wifi_only")
    }
}

/** The choices of the offline cache's storage budget. */
enum class CacheBudget(val megabytes: Int, val label: String) {
    Small(100, "100 MB"),
    Medium(200, "200 MB"),
    Large(500, "500 MB"),
    Huge(1024, "1 GB");

    val bytes: Long get() = megabytes * 1024L * 1024L

    companion object {
        val Default = Medium
    }
}
