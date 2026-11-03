namespace ReviewApp.Api.Enums;

// What an offline change (SyncOperation) or a deletion record (SyncTombstone) is about
public enum SyncEntityType
{
    Review,
    Collection,
    CollectionItem,
    Profile,
    Follow,
    Notification
}
