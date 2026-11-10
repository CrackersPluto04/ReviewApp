package com.reviewapp.android.data.sync

import com.reviewapp.android.data.local.offline.OutboxEntity
import com.reviewapp.android.data.local.offline.OutboxState
import com.reviewapp.android.data.remote.dto.CollectionCreatePayload
import com.reviewapp.android.data.remote.dto.CollectionItemPayload
import com.reviewapp.android.data.remote.dto.CollectionUpdatePayload
import com.reviewapp.android.data.remote.dto.FollowSetPayload
import com.reviewapp.android.data.remote.dto.NotificationSeenPayload
import com.reviewapp.android.data.remote.dto.ProfileUpdatePayload
import com.reviewapp.android.data.remote.dto.ReviewCreatePayload
import com.reviewapp.android.util.formatTimeLeft

/** Names the describer shows instead of ids (from the offline copy and the shadows). */
data class DescribeContext(
    /** Media key (`type:externalApiId`) -> title. */
    val mediaTitles: Map<String, String> = emptyMap(),
    /** Review id -> the title of its media. */
    val reviewTitles: Map<Int, String> = emptyMap(),
    /** Collection id -> name. */
    val collectionNames: Map<Int, String> = emptyMap(),
    /** User id -> username. */
    val usernames: Map<Int, String> = emptyMap(),
)

/** A queued change in plain language (pure, unit-tested). */
object OutboxDescriber {

    /** One line telling what the change does, e.g. "Wrote a review of Alien"; a payload that cannot be read becomes "A change". */
    fun describe(op: OutboxEntity, context: DescribeContext): String {
        val id = op.entityId
        val review = { id?.let { context.reviewTitles[it] } ?: "a title" }
        val collection = { id?.let { context.collectionNames[it] } ?: "a collection" }
        val user = { id?.let { context.usernames[it] } ?: "a user" }
        return try {
            when (op.type) {
                OpTypes.REVIEW_CREATE -> {
                    val media = OpJson.decodeFromString(ReviewCreatePayload.serializer(), op.payloadJson).media
                    "Wrote a review of ${context.mediaTitles["${media.type}:${media.externalApiId}"] ?: review()}"
                }
                OpTypes.REVIEW_UPDATE -> "Edited your review of ${review()}"
                OpTypes.REVIEW_DELETE -> "Deleted your review of ${review()}"
                OpTypes.COLLECTION_CREATE ->
                    "Created the collection ${OpJson.decodeFromString(CollectionCreatePayload.serializer(), op.payloadJson).fields.name}"
                OpTypes.COLLECTION_UPDATE ->
                    "Changed the collection ${OpJson.decodeFromString(CollectionUpdatePayload.serializer(), op.payloadJson).fields.name}"
                OpTypes.COLLECTION_DELETE -> "Deleted the collection ${collection()}"
                OpTypes.COLLECTION_ADD_ITEM, OpTypes.COLLECTION_REMOVE_ITEM -> {
                    val media = OpJson.decodeFromString(CollectionItemPayload.serializer(), op.payloadJson).media
                    val title = context.mediaTitles["${media.type}:${media.externalApiId}"] ?: "a title"
                    if (op.type == OpTypes.COLLECTION_ADD_ITEM) "Added $title to ${collection()}" else "Removed $title from ${collection()}"
                }
                OpTypes.COLLECTION_REORDER -> "Reordered ${collection()}"
                OpTypes.PROFILE_UPDATE -> {
                    val fields = OpJson.decodeFromString(ProfileUpdatePayload.serializer(), op.payloadJson).fields
                    when {
                        fields.bio != null && fields.profilePictureUrl != null -> "Updated your bio and profile picture"
                        fields.bio != null -> "Updated your bio"
                        else -> "Changed your profile picture"
                    }
                }
                OpTypes.FOLLOW_SET ->
                    if (OpJson.decodeFromString(FollowSetPayload.serializer(), op.payloadJson).follow) "Followed ${user()}" else "Unfollowed ${user()}"
                OpTypes.FOLLOWER_REMOVE -> "Removed ${user()} from your followers"
                OpTypes.NOTIFICATION_SEEN ->
                    if (OpJson.decodeFromString(NotificationSeenPayload.serializer(), op.payloadJson).seen) "Marked a notification as seen"
                    else "Marked a notification as unseen"
                OpTypes.NOTIFICATION_DELETE -> "Deleted a notification"
                OpTypes.NOTIFICATION_MARK_ALL_SEEN -> "Marked all notifications as seen"
                OpTypes.NOTIFICATION_DELETE_SEEN -> "Deleted the seen notifications"
                else -> "A change"
            }
        } catch (_: Exception) {
            "A change"
        }
    }

    /** Where the change is: waiting, being sent, retrying later, or needing the user. */
    fun status(op: OutboxEntity, nowMillis: Long): String = when (op.state) {
        OutboxState.IN_FLIGHT -> "Sending…"
        OutboxState.CONFLICT -> "Needs your attention: ${reasonMessage(op.lastReason)}"
        OutboxState.REJECTED -> "Couldn't be synced: ${reasonMessage(op.lastReason)}"
        else -> when {
            op.attempts == 0 || op.nextAttemptAt <= nowMillis -> "Waiting to sync"
            else -> "Trying again in ${formatTimeLeft(op.nextAttemptAt - nowMillis)}. ${reasonMessage(op.lastReason)}"
        }
    }

    /** The backend's reasons (the API's SyncReason) for people. */
    fun reasonMessage(reason: String?): String = when (reason) {
        "field_conflict" -> "It was changed elsewhere too."
        "deleted" -> "It was deleted elsewhere."
        "modified_since" -> "It was edited elsewhere since."
        "already_reviewed" -> "You already reviewed this title."
        "name_taken" -> "You already have a collection with this name."
        "collection_deleted" -> "The collection was deleted elsewhere."
        "invalid" -> "Something in this change isn't valid."
        "locked" -> "The Favourites collection can't be changed this way."
        "media_not_found" -> "This title is no longer available."
        "self" -> "You can't follow yourself."
        "user_not_found" -> "This user no longer exists."
        "source_unavailable" -> "The movie and music databases can't be reached right now."
        "deferred" -> "Waiting for its turn."
        "busy" -> "The server was busy."
        "server_error" -> "The server had a problem."
        "dependency" -> "Waiting for an earlier change."
        else -> "Something went wrong."
    }
}
