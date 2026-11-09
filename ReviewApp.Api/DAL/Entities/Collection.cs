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

    // Set by CollectionService on every change of the collection or its items (rename, visibility, add, remove,
    // reorder), which also moves RowVersion, so the sync's changes feed sees item changes as a changed collection.
    // Has a database default for the rows the AddDefaultCollection trigger inserts.
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // SQL Server rowversion: changes on every write of the row. The base version an offline edit is checked against,
    // and the cursor of the sync's changes feed. Not a concurrency token, so web edits keep "last write wins".
    public long RowVersion { get; set; }

    public ICollection<CollectionMedia> CollectionMedias { get; set; } = [];
}
