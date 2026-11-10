namespace ReviewApp.Api.DTOs;

/// <summary>
/// One suggestion: a normal media item (it links to its media page) and how closely it relates to the photo.
/// </summary>
public record RecognizedMediaDto
{
    public MediaDto Media { get; init; } = null!;
    /// <summary><c>direct</c>, <c>universe</c>, <c>people</c> or <c>similar</c>, closest first. Results are ranked by this tier before anything else.</summary>
    public string Tier { get; init; } = string.Empty;
}

/// <summary>
/// What the AI saw in the photo.
/// </summary>
public record RecognitionSubjectDto
{
    public string Label { get; init; } = string.Empty;
    /// <summary>One of <c>figure</c>, <c>statue</c>, <c>poster</c>, <c>sticker</c>, <c>album_cover</c>, <c>costume</c>, <c>merchandise</c>, <c>other</c>.</summary>
    public string Kind { get; init; } = string.Empty;
}

/// <summary>
/// Availability of camera recognition for the caller and what is left of the daily allowance.
/// </summary>
public record RecognitionAllowanceDto
{
    /// <summary>False when the feature is switched off; the clients then hide its entry.</summary>
    public bool Enabled { get; init; }
    /// <summary>Whether the user may use the feature (Premium). The counters are null when not.</summary>
    public bool Premium { get; init; }
    /// <summary>Recognitions allowed per day (the day starts at UTC midnight).</summary>
    public int? Limit { get; init; }
    public int? Remaining { get; init; }
    /// <summary>When the allowance next resets, UTC.</summary>
    public DateTime? ResetsAt { get; init; }
}

/// <summary>
/// Response of <c>POST api/Recognition</c>: the outcome, what was seen and the first page of ranked media.
/// </summary>
public record RecognitionResponseDto
{
    /// <summary>Null when there is nothing to page through; "Show more" needs it.</summary>
    public string? RecognitionId { get; init; }
    /// <summary><c>recognised</c>, <c>nothing_recognised</c>, <c>not_media_related</c> or <c>person_not_identified</c>.</summary>
    public string Outcome { get; init; } = string.Empty;
    /// <summary>A plain-text sentence beginning "We saw ...".</summary>
    public string Summary { get; init; } = string.Empty;
    public RecognitionSubjectDto? Subject { get; init; }
    public List<RecognizedMediaDto> Items { get; init; } = [];
    /// <summary>Size of the whole stored list, not just this page.</summary>
    public int Total { get; init; }
    public bool HasMore { get; init; }
    /// <summary>The <c>offset</c> to pass to the items endpoint for the next page.</summary>
    public int NextOffset { get; init; }
    /// <summary>Some TMDb / Spotify lookups failed, so the list may be missing items.</summary>
    public bool Incomplete { get; init; }
    public RecognitionAllowanceDto Allowance { get; init; } = new();
}

/// <summary>
/// Response of <c>GET api/Recognition/{id}/items</c>: a further slice of a stored result.
/// </summary>
public record RecognitionItemsDto
{
    public List<RecognizedMediaDto> Items { get; init; } = [];
    public int Total { get; init; }
    public bool HasMore { get; init; }
    public int NextOffset { get; init; }
}
