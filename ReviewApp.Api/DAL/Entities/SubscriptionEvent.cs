using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DAL.Entities;

// Provider notifications that were received, for deduplication and audit. Payloads are never stored.
public class SubscriptionEvent
{
    public int ID { get; set; }

    [MaxLength(100)]
    public string ProviderEventID { get; set; } = string.Empty;

    [MaxLength(100)]
    public string Type { get; set; } = string.Empty;

    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; } // null while unprocessed, so a provider retry can pick it up again
}
