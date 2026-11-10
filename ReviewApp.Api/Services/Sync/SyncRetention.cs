namespace ReviewApp.Api.Services.Sync;

/// <summary>
/// Retention of the offline sync's bookkeeping.
/// </summary>
public static class SyncRetention
{
    /// <summary>
    /// How long applied offline changes (<c>SyncOperation</c>) and deletion records (<c>SyncTombstone</c>) are
    /// kept. Longer than the 7 day offline period of the Android app, so a change sent again after a lost answer is
    /// still recognised, and a device that was away for longer than this gets a full resync instead of a delta.
    /// </summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);
}
