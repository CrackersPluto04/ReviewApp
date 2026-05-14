using ReviewApp.Api.Enums;
using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DAL.Entities;

public class Collection
{
    public int ID { get; set; }

    [Required]
    public int UserID { get; set; }
    public User User { get; set; } = null!;

    [Required]
    public string Name { get; set; } = string.Empty;
    public VisibilityLevel VisibilityLevel { get; set; } = VisibilityLevel.Private;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<CollectionMedia> CollectionMedias { get; set; } = [];
}
