package com.reviewapp.android.data.local.offline

import android.content.Context
import android.util.Log
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import java.io.File

/** The open offline database and whose it is. */
class OpenDatabase(val userId: Int, val db: OfflineDatabase)

/**
 * Opens, closes and deletes the per-user offline databases (`offline_<userId>.db` in `noBackupFilesDir/offline`,
 * which Android never backs up or transfers). At most one is open: the logged-in user's.
 *
 * A file whose key is lost or wrong can never be read again, so it is deleted and started empty; the next sync
 * fills it from the backend. Only changes still waiting in that file are lost, which can only happen when the
 * Keystore itself lost the key (an app data restore onto another device, a keystore reset).
 */
class DatabaseManager(
    context: Context,
    private val keys: DatabaseKeyStore,
    /** The user's poster copies go with their database. */
    private val images: OfflineImageStore? = null,
) {
    private val appContext = context.applicationContext
    private val dir = File(appContext.noBackupFilesDir, "offline")
    private val mutex = Mutex()

    private val _current = MutableStateFlow<OpenDatabase?>(null)
    val current: StateFlow<OpenDatabase?> = _current.asStateFlow()

    /** The database file of a user. */
    fun fileFor(userId: Int) = File(dir, "offline_$userId.db")

    /** Whether the user has an offline copy on this phone. */
    fun exists(userId: Int) = fileFor(userId).exists()

    /** Bytes the user's offline copy takes on disk (the database with its journal files, and the posters). */
    fun sizeBytes(userId: Int): Long =
        filesOf(fileFor(userId)).sumOf { if (it.exists()) it.length() else 0L } + (images?.sizeBytes(userId) ?: 0L)

    /** Opens [userId]'s database (creating it), closing another user's first. */
    suspend fun open(userId: Int): OfflineDatabase = mutex.withLock {
        _current.value?.let { open ->
            if (open.userId == userId) return@withLock open.db
            open.db.close()
            _current.value = null
        }
        val db = withContext(Dispatchers.IO) { openReadable(userId) }
        _current.value = OpenDatabase(userId, db)
        db
    }

    /** Closes the open database, if any. */
    suspend fun close() = mutex.withLock {
        _current.value?.db?.close()
        _current.value = null
    }

    /** Removes [userId]'s offline copy and its key. */
    suspend fun delete(userId: Int) = mutex.withLock {
        _current.value?.takeIf { it.userId == userId }?.let {
            it.db.close()
            _current.value = null
        }
        withContext(Dispatchers.IO) {
            deleteFiles(fileFor(userId))
            keys.delete(userId)
            images?.deleteUser(userId)
        }
    }

    /**
     * The other users whose offline copy on this phone still holds changes the backend hasn't accepted (their
     * session ended without a logout, e.g. revoked). Each copy is opened on its own, only to count, and closed.
     */
    suspend fun othersWithChanges(exceptUserId: Int?): List<Int> = mutex.withLock {
        withContext(Dispatchers.IO) {
            val ids = dir.listFiles().orEmpty().mapNotNull { FILE_NAME.matchEntire(it.name)?.groupValues?.get(1)?.toIntOrNull() }
            ids.filter { it != exceptUserId && it != _current.value?.userId }.filter { userId ->
                val key = (keys.read(userId) as? StoredKey.Found)?.key ?: return@filter false
                val db = OfflineDatabase.open(appContext, fileFor(userId), key)
                try {
                    db.outbox().count() > 0
                } catch (e: Exception) {
                    Log.w(TAG, "Could not read another account's offline copy", e)
                    false
                } finally {
                    db.close()
                }
            }
        }
    }

    private suspend fun openReadable(userId: Int): OfflineDatabase {
        dir.mkdirs()
        val file = fileFor(userId)
        val key = when (val stored = keys.read(userId)) {
            is StoredKey.Found -> stored.key
            else -> {
                // Without its key an existing file is unreadable
                if (file.exists()) Log.w(TAG, "Offline database key ${stored::class.simpleName}, starting a new copy")
                deleteFiles(file)
                keys.create(userId)
            }
        }
        val db = OfflineDatabase.open(appContext, file, key)
        return try {
            db.openHelper.writableDatabase
            db
        } catch (e: Exception) {
            // A wrong key ("file is not a database") or a corrupt file
            Log.w(TAG, "Offline database could not be opened, starting a new copy", e)
            db.close()
            deleteFiles(file)
            OfflineDatabase.open(appContext, file, keys.create(userId)).also { it.openHelper.writableDatabase }
        }
    }

    private fun filesOf(file: File) = listOf("", "-wal", "-shm", "-journal").map { File(file.path + it) }

    private fun deleteFiles(file: File) = filesOf(file).forEach { it.delete() }

    private companion object {
        const val TAG = "DatabaseManager"
        val FILE_NAME = Regex("""offline_(\d+)\.db""")
    }
}
