package com.reviewapp.android.data.local

import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import com.reviewapp.android.data.sync.ClockReading
import com.reviewapp.android.domain.model.AuthUser
import kotlinx.coroutines.flow.first
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json

/** The logged-in user as the backend last confirmed it, and when ([confirmedAt]). */
@Serializable
data class CachedSession(val user: CachedUser, val confirmedAt: ClockReading)

@Serializable
data class CachedUser(
    val id: Int,
    val username: String,
    val profilePictureUrl: String? = null,
    val isEmailConfirmed: Boolean = false,
    val isPremium: Boolean = false,
    val entitlements: Set<String> = emptySet(),
) {
    fun toDomain() = AuthUser(id, username, profilePictureUrl, isEmailConfirmed, isPremium, entitlements)

    companion object {
        fun from(user: AuthUser) =
            CachedUser(user.id, user.username, user.profilePictureUrl, user.isEmailConfirmed, user.isPremium, user.entitlements)
    }
}

/**
 * Keeps the last confirmed user on the device so offline mode can start without the backend. Encrypted like the
 * session cookie ([CryptoStore]) in the same `session` DataStore, which is excluded from backups.
 */
class SessionCache(
    private val dataStore: DataStore<Preferences>,
    private val crypto: CryptoStore,
) {
    suspend fun read(): CachedSession? {
        val encrypted = dataStore.data.first()[KEY] ?: return null
        val decoded = crypto.decrypt(encrypted) ?: return null
        return try {
            json.decodeFromString<CachedSession>(decoded)
        } catch (_: Exception) {
            null
        }
    }

    /** The backend just confirmed [user]. */
    suspend fun confirm(user: AuthUser, at: ClockReading) = write(CachedSession(CachedUser.from(user), at))

    /** The user changed locally (e.g. a new avatar); the confirmation time stays. */
    suspend fun updateUser(user: AuthUser) {
        val current = read() ?: return
        if (current.user.id == user.id) write(current.copy(user = CachedUser.from(user)))
    }

    suspend fun clear() {
        dataStore.edit { it.remove(KEY) }
    }

    private suspend fun write(session: CachedSession) {
        val encrypted = crypto.encrypt(json.encodeToString(CachedSession.serializer(), session))
        dataStore.edit { it[KEY] = encrypted }
    }

    private companion object {
        val KEY = stringPreferencesKey("cached_session")
        val json = Json { ignoreUnknownKeys = true }
    }
}
