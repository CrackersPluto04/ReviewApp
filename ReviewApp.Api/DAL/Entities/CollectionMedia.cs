using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DAL.Entities;

public class CollectionMedia
{
    [Required]
    public int CollectionID { get; set; }
    public Collection Collection { get; set; } = null!;

    [Required]
    public int MediaID { get; set; }
    public Media Media { get; set; } = null!;

    public int OrderIndex { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
