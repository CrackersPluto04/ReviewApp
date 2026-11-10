using ReviewApp.Api.Enums;
using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DAL.Entities;

/// <summary>
/// A user's named collection of media. Names are unique per user.
/// </summary>
public class Collection
{
    /// <summary>
    /// The name of the default collection, created for every user by the <c>AddDefaultCollection</c> trigger. It
    /// cannot be renamed or deleted, so this name always identifies it (and no other collection can take it, because
    /// names are unique per user).
    /// </summary>
    public const string DefaultName = "Favourites";

    public int ID { get; set; }

    [Required]
    public int UserID { get; set; }
    public User User { get; set; } = null!;

    [Required, MaxLength(50)]
    public string Name { get; set; } = string.Empty;
    public VisibilityLevel VisibilityLevel { get; set; } = VisibilityLevel.Private;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Set by <c>CollectionService</c> on every change of the collection or its items (rename, visibility, add,
    /// remove, reorder), which also moves <see cref="RowVersion"/>, so the sync's changes feed sees item changes as
    /// a changed collection. Has a database default for the rows the <c>AddDefaultCollection</c> trigger inserts.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// SQL Server <c>rowversion</c>: changes on every write of the row. It is the base version an offline edit is
    /// checked against and the cursor of the sync's changes feed. Deliberately not a concurrency token, so web edits
    /// keep "last write wins".
    /// </summary>
    public long RowVersion { get; set; }

    public ICollection<CollectionMedia> CollectionMedias { get; set; } = [];
}
