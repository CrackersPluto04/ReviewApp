namespace ReviewApp.Api.Services.Sync;

public static class SyncRetention
{
    // How long applied offline changes (SyncOperation) and deletion records (SyncTombstone) are kept.
    // Longer than the 7 day offline period of the Android app, so a change sent again after a lost answer is still
    // recognised, and a device that was away for longer than this gets a full resync instead of a delta.
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);
}
