namespace ReviewApp.Api.DTOs;

// One suggestion: a normal media item (links to its media page) and how closely it relates to the photo.
// Tier: "direct" | "universe" | "people" | "similar"
public record RecognizedMediaDto
{
    public MediaDto Media { get; init; } = null!;
    public string Tier { get; init; } = string.Empty;
}

public record RecognitionSubjectDto
{
    public string Label { get; init; } = string.Empty;
    // "figure" | "statue" | "poster" | "sticker" | "album_cover" | "costume" | "merchandise" | "other"
    public string Kind { get; init; } = string.Empty;
}

public record RecognitionAllowanceDto
{
    // False: the feature is switched off, the clients hide its entry
    public bool Enabled { get; init; }
    // Whether the user may use the feature (Premium); the counters are null when not
    public bool Premium { get; init; }
    public int? Limit { get; init; }
    public int? Remaining { get; init; }
    public DateTime? ResetsAt { get; init; }
}

// POST api/Recognition
public record RecognitionResponseDto
{
    // Null when there is nothing to page through ("Show more" needs it)
    public string? RecognitionId { get; init; }
    // "recognised" | "nothing_recognised" | "not_media_related" | "person_not_identified"
    public string Outcome { get; init; } = string.Empty;
    // "We saw ..." in plain text
    public string Summary { get; init; } = string.Empty;
    public RecognitionSubjectDto? Subject { get; init; }
    public List<RecognizedMediaDto> Items { get; init; } = [];
    public int Total { get; init; }
    public bool HasMore { get; init; }
    public int NextOffset { get; init; }
    // Some TMDb / Spotify lookups failed, the list may be missing items
    public bool Incomplete { get; init; }
    public RecognitionAllowanceDto Allowance { get; init; } = new();
}

// GET api/Recognition/{id}/items
public record RecognitionItemsDto
{
    public List<RecognizedMediaDto> Items { get; init; } = [];
    public int Total { get; init; }
    public bool HasMore { get; init; }
    public int NextOffset { get; init; }
}
