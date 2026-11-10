namespace ReviewApp.Api.Enums;

/// <summary>
/// What an offline change (<c>SyncOperation</c>) or a deletion record (<c>SyncTombstone</c>) is about. Tombstones
/// only use <see cref="Review"/> and <see cref="Collection"/>.
/// </summary>
public enum SyncEntityType
{
    Review,
    Collection,
    CollectionItem,
    Profile,
    Follow,
    Notification
}
