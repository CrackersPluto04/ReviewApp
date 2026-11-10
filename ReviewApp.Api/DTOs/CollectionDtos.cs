using ReviewApp.Api.Enums;
using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DTOs;

/// <summary>
/// Request body for creating a collection. Names are unique per user.
/// </summary>
public record CreateCollectionDto
{
    [Required, MaxLength(50)]
    public string Name { get; init; } = string.Empty;
    /// <summary>Private unless the owner chooses otherwise.</summary>
    public VisibilityLevel VisibilityLevel { get; init; } = VisibilityLevel.Private;
}

/// <summary>
/// Request body for renaming a collection or changing its visibility. The permanent Favourites collection refuses it.
/// </summary>
public record UpdateCollectionDto
{
    [Required]
    public int CollectionID { get; init; }
    [MaxLength(50)]
    public string Name { get; init; } = string.Empty;
    public VisibilityLevel VisibilityLevel { get; init; } = VisibilityLevel.Private;
}

/// <summary>
/// Identifies the title to add to a collection by its source and ID. The title is fetched and cached on first use.
/// </summary>
public record AddMediaToCollectionDto
{
    [Required]
    public MediaType Type { get; init; }
    [Required]
    public string ExternalApiID { get; init; } = string.Empty;
}

/// <summary>
/// Request body for moving one title within a collection.
/// </summary>
public record ReorderMediaDto
{
    /// <summary>The stored (database) media ID, as returned in <see cref="CollectionMediaDto.DbMediaID"/>, not the external ID.</summary>
    [Required]
    public int DbMediaID { get; init; }
    /// <summary>The title's final position in the order.</summary>
    [Required]
    public int NewOrderIndex { get; init; }
}

/// <summary>
/// A collection together with its titles in the saved order.
/// </summary>
public record CollectionWithMediasDto
{
    public CollectionDto Collection { get; init; } = null!;
    public List<CollectionMediaDto> MediaItems { get; init; } = [];
}

/// <summary>
/// A collection as shown in lists and headers.
/// </summary>
public record CollectionDto
{
    public int ID { get; init; }
    [MaxLength(50)]
    public string Name { get; init; } = string.Empty;
    public VisibilityLevel VisibilityLevel { get; init; }
    public string? CreatedAt { get; init; }
    public int MediaCount { get; init; }
    /// <summary>Whether the viewer owns the collection, so the client can offer editing.</summary>
    public bool IsOwner { get; init; }
    /// <summary>The permanent Favourites collection, which cannot be renamed, edited or deleted.</summary>
    public bool IsDefault { get; init; }
}

/// <summary>
/// One title in a collection.
/// </summary>
public record CollectionMediaDto
{
    /// <summary>The stored (database) media ID, used to reorder.</summary>
    public int DbMediaID { get; init; }
    public MediaDto Media { get; init; } = null!;
    public int OrderIndex { get; init; }
    public DateTime AddedAt { get; init; }
}

/// <summary>
/// One title's membership of one of the caller's collections. Clients cache the full list to show favourite and
/// collection state on media cards.
/// </summary>
public record CollectionMembershipDto
{
    public int CollectionID { get; init; }
    public MediaType MediaType { get; init; }
    public string ExternalApiID { get; init; } = string.Empty;
}
