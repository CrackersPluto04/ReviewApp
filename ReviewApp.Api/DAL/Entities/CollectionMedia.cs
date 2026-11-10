using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DAL.Entities;

/// <summary>
/// Join entity: one media item in one collection. The key is (<c>CollectionID</c>, <c>MediaID</c>), so a title can be
/// in a collection only once, and it cascades from both sides.
/// </summary>
public class CollectionMedia
{
    [Required]
    public int CollectionID { get; set; }
    public Collection Collection { get; set; } = null!;

    [Required]
    public int MediaID { get; set; }
    public Media Media { get; set; } = null!;

    /// <summary>Position in the collection, 0-based and gap-free. New items go at the end.</summary>
    public int OrderIndex { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
