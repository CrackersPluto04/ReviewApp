namespace ReviewApp.Api.Enums;

/// <summary>
/// Who may see a review or a collection. New content is private unless its author chooses otherwise. The values are
/// stored and sent as integers, so they are explicit and must not change.
/// </summary>
public enum VisibilityLevel
{
    /// <summary>Only the owner.</summary>
    Private = 0,
    /// <summary>Everyone, including anonymous visitors.</summary>
    Public = 1,
    /// <summary>The owner and the users who follow them.</summary>
    FollowersOnly = 2
}
