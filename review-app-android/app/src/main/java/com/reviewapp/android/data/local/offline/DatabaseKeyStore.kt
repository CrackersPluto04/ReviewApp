package com.reviewapp.android.data.local.offline

import android.util.Base64
import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import com.reviewapp.android.data.local.CryptoStore
import kotlinx.coroutines.flow.first
import java.security.SecureRandom

sealed interface StoredKey {
    class Found(val key: ByteArray) : StoredKey
    data object Missing : StoredKey
    /** Stored, but the Keystore key that wraps it is gone (restore to another device, keystore reset). */
    data object Lost : StoredKey
}

/**
 * The random 32 byte SQLCipher key of each user's offline database, wrapped by its own Android Keystore key
 * ([CryptoStore] alias `review_app_db_key`) and kept in the `session` DataStore (excluded from backups).
 */
class DatabaseKeyStore(
    private val dataStore: DataStore<Preferences>,
    private val crypto: CryptoStore = CryptoStore("review_app_db_key"),
) {
    suspend fun read(userId: Int): StoredKey {
        val wrapped = dataStore.data.first()[keyOf(userId)] ?: return StoredKey.Missing
        val encoded = crypto.decrypt(wrapped) ?: return StoredKey.Lost
        return runCatching { Base64.decode(encoded, Base64.NO_WRAP) }.getOrNull()
            ?.takeIf { it.size == KEY_BYTES }
            ?.let { StoredKey.Found(it) }
            ?: StoredKey.Lost
    }

    /** A new key for [userId], replacing any stored one. */
    suspend fun create(userId: Int): ByteArray {
        val key = ByteArray(KEY_BYTES).also { SecureRandom().nextBytes(it) }
        val wrapped = crypto.encrypt(Base64.encodeToString(key, Base64.NO_WRAP))
        dataStore.edit { it[keyOf(userId)] = wrapped }
        return key
    }

    suspend fun delete(userId: Int) {
        dataStore.edit { it.remove(keyOf(userId)) }
    }

    private fun keyOf(userId: Int) = stringPreferencesKey("offline_db_key_$userId")

    private companion object {
        const val KEY_BYTES = 32
    }
}
