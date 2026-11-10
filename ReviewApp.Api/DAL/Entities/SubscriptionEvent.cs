using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DAL.Entities;

/// <summary>
/// A provider notification that was received, for deduplication and audit. Payloads are never stored.
/// </summary>
public class SubscriptionEvent
{
    public int ID { get; set; }

    /// <summary>The provider's event ID, unique: a redelivery of the same event is recognised by it.</summary>
    [MaxLength(100)]
    public string ProviderEventID { get; set; } = string.Empty;

    [MaxLength(100)]
    public string Type { get; set; } = string.Empty;

    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    /// <summary>Null while unprocessed, so a provider retry can pick the event up again after a failure.</summary>
    public DateTime? ProcessedAt { get; set; }
}
