package com.reviewapp.android.data.local.offline

import android.content.Context
import androidx.room.Dao
import androidx.room.Database
import androidx.room.Query
import androidx.room.Room
import androidx.room.RoomDatabase
import androidx.room.Upsert
import kotlinx.coroutines.flow.Flow
import net.zetetic.database.sqlcipher.SupportOpenHelperFactory
import java.io.File

@Dao
interface SyncMetaDao {
    @Query("SELECT value FROM sync_meta WHERE `key` = :key")
    suspend fun get(key: String): String?

    @Query("SELECT value FROM sync_meta WHERE `key` = :key")
    fun observe(key: String): Flow<String?>

    @Upsert
    suspend fun put(entry: SyncMetaEntity)

    suspend fun put(key: String, value: String) = put(SyncMetaEntity(key, value))
}

@Dao
interface OutboxDao {
    /** Changes the backend hasn't accepted yet (waiting, being sent, or in Sync issues). */
    @Query("SELECT COUNT(*) FROM outbox")
    fun observeCount(): Flow<Int>

    @Query("SELECT COUNT(*) FROM outbox")
    suspend fun count(): Int
}

/**
 * The offline copy of one user's data (one file per user, so a queue can never be sent under another account),
 * opened by [DatabaseManager]. Encrypted with SQLCipher; the 32 byte key is generated per file and kept wrapped by
 * the Android Keystore.
 *
 * Migrations: version 1 is still being shaped while offline mode is built (nothing is installed with it yet).
 * Once released, every schema change needs an explicit [androidx.room.migration.Migration] tested against the
 * exported schemas in `app/schemas/`, never a destructive one: the outbox holds changes that exist nowhere else.
 */
@Database(
    entities = [
        ReviewEntity::class,
        CollectionEntity::class,
        CollectionItemEntity::class,
        MediaEntity::class,
        ProfileEntity::class,
        KnownUserEntity::class,
        FollowEntity::class,
        NotificationEntity::class,
        AchievementsJsonEntity::class,
        SnapshotEntity::class,
        ServerShadowEntity::class,
        OutboxEntity::class,
        IdMapEntity::class,
        DraftEntity::class,
        PendingUnlockEntity::class,
        SyncMetaEntity::class,
    ],
    version = 1,
    exportSchema = true,
)
abstract class OfflineDatabase : RoomDatabase() {
    abstract fun syncMeta(): SyncMetaDao
    abstract fun outbox(): OutboxDao

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

        /**
         * Opens (or creates) the encrypted database [file] (an absolute path); a wrong [passphrase] fails on first use.
         * The factory uses [passphrase] whenever Room (re)opens the connection, so the array must stay untouched.
         */
        fun open(context: Context, file: File, passphrase: ByteArray): OfflineDatabase {
            loadSqlCipher()
            return Room.databaseBuilder(context.applicationContext, OfflineDatabase::class.java, file.absolutePath)
                .openHelperFactory(SupportOpenHelperFactory(passphrase))
                .build()
        }
    }
}
