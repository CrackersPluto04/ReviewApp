package com.reviewapp.android.data.local.offline

import android.content.Context
import androidx.room.Dao
import androidx.room.Database
import androidx.room.Entity
import androidx.room.PrimaryKey
import androidx.room.Query
import androidx.room.Room
import androidx.room.RoomDatabase
import androidx.room.Upsert
import kotlinx.coroutines.flow.Flow
import net.zetetic.database.sqlcipher.SupportOpenHelperFactory

/** Small key/value facts about the local copy (sync cursor, last finished sync, download progress). */
@Entity(tableName = "sync_meta")
data class SyncMetaEntity(
    @PrimaryKey val key: String,
    val value: String,
)

@Dao
interface SyncMetaDao {
    @Query("SELECT value FROM sync_meta WHERE `key` = :key")
    suspend fun get(key: String): String?

    @Query("SELECT value FROM sync_meta WHERE `key` = :key")
    fun observe(key: String): Flow<String?>

    @Upsert
    suspend fun put(entry: SyncMetaEntity)
}

/**
 * The offline copy of one user's data (one file per user, so a queue can never be sent under
 * another account). Encrypted with SQLCipher; the 32 byte key is generated per file and kept
 * wrapped by the Android Keystore. Schema changes need an explicit migration, never a
 * destructive one: the outbox holds changes that exist nowhere else.
 */
@Database(entities = [SyncMetaEntity::class], version = 1, exportSchema = true)
abstract class OfflineDatabase : RoomDatabase() {
    abstract fun syncMeta(): SyncMetaDao

    companion object {
        @Volatile private var libraryLoaded = false

        private fun loadSqlCipher() {
            if (libraryLoaded) return
            synchronized(this) {
                if (!libraryLoaded) {
                    System.loadLibrary("sqlcipher")
                    libraryLoaded = true
                }
            }
        }

        /** Opens (or creates) the encrypted database file [name]; a wrong [passphrase] fails on first use. */
        fun open(context: Context, name: String, passphrase: ByteArray): OfflineDatabase {
            loadSqlCipher()
            return Room.databaseBuilder(context.applicationContext, OfflineDatabase::class.java, name)
                .openHelperFactory(SupportOpenHelperFactory(passphrase))
                .build()
        }
    }
}
