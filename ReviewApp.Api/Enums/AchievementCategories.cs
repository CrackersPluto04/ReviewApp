namespace ReviewApp.Api.Enums;

/// <summary>
/// The categories an achievement is filed under. Flags, so one achievement can belong to several (for example
/// <c>Movie | Review</c>); the clients filter by them.
/// </summary>
[Flags]
public enum AchievementCategories
{
    None = 0,
    Movie = 1,
    Series = 2,
    Music = 4,
    Review = 8,
    Reply = 16,
    Collection = 32
}
