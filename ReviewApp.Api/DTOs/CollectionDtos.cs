using ReviewApp.Api.Enums;
using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DTOs;

public record CreateCollectionDto
{
    [Required]
    public string Name { get; init; } = string.Empty;
    public VisibilityLevel VisibilityLevel { get; init; } = VisibilityLevel.Private;
}

public record UpdateCollectionDto
{
    [Required]
    public int CollectionID { get; init; }
    public string Name { get; init; } = string.Empty;
    public VisibilityLevel VisibilityLevel { get; init; } = VisibilityLevel.Private;
}

public record AddMediaToCollectionDto
{
    [Required]
    public MediaType Type { get; init; }
    [Required]
    public string ExternalApiID { get; init; } = string.Empty;
}

public record ReorderMediaDto
{
    [Required]
    public int MediaID { get; init; }
    [Required]
    public int NewOrderIndex { get; init; }
}

public record CollectionWithMediasDto
{
    public CollectionDto Collection { get; init; } = null!;
    public List<CollectionMediaDto> MediaItems { get; init; } = [];
}

public record CollectionDto
{
    public int ID { get; init; }
    public string Name { get; init; } = string.Empty;
    public VisibilityLevel VisibilityLevel { get; init; }
    public DateTime CreatedAt { get; init; }
    public int MediaCount { get; init; }
    public bool IsOwner { get; init; }
}

public record CollectionMediaDto
{
    public MediaDto Media { get; init; } = null!;
    public int OrderIndex { get; init; }
    public DateTime AddedAt { get; init; }
}
