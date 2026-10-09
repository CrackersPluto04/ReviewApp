using ReviewApp.Api.Enums;
using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DAL.Entities;

public class Collection
{
    // Created for every user by the AddDefaultCollection trigger. It can't be renamed or deleted,
    // so this name always identifies it (and no other collection can take it, names are unique per user).
    public const string DefaultName = "Favourites";

    public int ID { get; set; }

    [Required]
    public int UserID { get; set; }
    public User User { get; set; } = null!;

    [Required, MaxLength(50)]
    public string Name { get; set; } = string.Empty;
    public VisibilityLevel VisibilityLevel { get; set; } = VisibilityLevel.Private;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<CollectionMedia> CollectionMedias { get; set; } = [];
}
