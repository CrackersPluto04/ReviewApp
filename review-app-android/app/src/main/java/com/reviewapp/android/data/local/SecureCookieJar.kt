package com.reviewapp.android.data.local

import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.Serializable
import kotlinx.serialization.decodeFromString
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import okhttp3.Cookie
import okhttp3.CookieJar
import okhttp3.HttpUrl

/**
 * Cookie jar that keeps the API's httpOnly session cookie (`__Host-jwt_token`) across app restarts,
 * the same way a browser does for the web app. Persistent cookies are stored encrypted with
 * [CryptoStore] in the `session` DataStore, session cookies only live in memory.
 *
 * OkHttp calls the jar from its own worker threads, so the blocking DataStore access is fine here.
 */
class SecureCookieJar(
    private val dataStore: DataStore<Preferences>,
    private val crypto: CryptoStore,
) : CookieJar {

    private val lock = Any()
    private var cookies: MutableList<Cookie>? = null

    override fun saveFromResponse(url: HttpUrl, cookies: List<Cookie>) {
        if (cookies.isEmpty()) return
        synchronized(lock) {
            val current = loaded()
            for (cookie in cookies) {
                current.removeAll { it.sameAs(cookie) }
                // A delete arrives as an already expired cookie, it only removes the old one
                if (cookie.expiresAt > System.currentTimeMillis()) current += cookie
            }
            persist(current)
        }
    }

    override fun loadForRequest(url: HttpUrl): List<Cookie> = synchronized(lock) {
        val current = loaded()
        if (current.removeAll { it.expiresAt <= System.currentTimeMillis() }) persist(current)
        current.filter { it.matches(url) }
    }

    /** True while an unexpired session cookie is stored, a cheap check before calling the API. */
    fun hasSession(): Boolean = synchronized(lock) {
        loaded().any { it.name == SESSION_COOKIE && it.expiresAt > System.currentTimeMillis() }
    }

    fun clear() = synchronized(lock) {
        val current = loaded()
        current.clear()
        persist(current)
    }

    private fun loaded(): MutableList<Cookie> =
        cookies ?: readStored().toMutableList().also { cookies = it }

    private fun readStored(): List<Cookie> {
        val encrypted = runBlocking { dataStore.data.first()[COOKIES_KEY] } ?: return emptyList()
        val decoded = crypto.decrypt(encrypted) ?: return emptyList()
        return try {
            json.decodeFromString<List<StoredCookie>>(decoded).mapNotNull { it.toCookie() }
        } catch (_: Exception) {
            emptyList()
        }
    }

    private fun persist(current: List<Cookie>) {
        val stored = current.filter { it.persistent }.map { StoredCookie.from(it) }
        runBlocking {
            dataStore.edit { prefs ->
                if (stored.isEmpty()) prefs.remove(COOKIES_KEY)
                else prefs[COOKIES_KEY] = crypto.encrypt(json.encodeToString(stored))
            }
        }
    }

    private fun Cookie.sameAs(other: Cookie) =
        name == other.name && domain == other.domain && path == other.path

    @Serializable
    private data class StoredCookie(
        val name: String,
        val value: String,
        val expiresAt: Long,
        val domain: String,
        val path: String,
        val secure: Boolean,
        val httpOnly: Boolean,
        val hostOnly: Boolean,
    ) {
        fun toCookie(): Cookie? = try {
            Cookie.Builder()
                .name(name)
                .value(value)
                .expiresAt(expiresAt)
                .path(path)
                .apply { if (hostOnly) hostOnlyDomain(domain) else domain(domain) }
                .apply { if (secure) secure() }
                .apply { if (httpOnly) httpOnly() }
                .build()
        } catch (_: IllegalArgumentException) {
            null
        }

        companion object {
            fun from(c: Cookie) = StoredCookie(
                c.name, c.value, c.expiresAt, c.domain, c.path, c.secure, c.httpOnly, c.hostOnly,
            )
        }
    }

    companion object {
        const val SESSION_COOKIE = "__Host-jwt_token"
        private val COOKIES_KEY = stringPreferencesKey("cookies")
        private val json = Json { ignoreUnknownKeys = true }
    }
}
