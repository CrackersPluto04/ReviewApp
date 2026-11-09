package com.reviewapp.android.data.local.offline

import android.content.Context
import androidx.room.Database
import androidx.room.Room
import androidx.room.RoomDatabase
import net.zetetic.database.sqlcipher.SupportOpenHelperFactory
import java.io.File

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
    abstract fun reviews(): ReviewDao
    abstract fun collections(): CollectionDao
    abstract fun media(): MediaDao
    abstract fun profile(): ProfileDao
    abstract fun follows(): FollowDao
    abstract fun notifications(): NotificationDao
    abstract fun snapshots(): SnapshotDao
    abstract fun shadows(): ShadowDao
    abstract fun idMap(): IdMapDao
    abstract fun drafts(): DraftDao
    abstract fun pendingUnlocks(): PendingUnlockDao

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
