namespace ReviewApp.Api.Enums;

// Flags: an achievement can belong to several categories (e.g. Movie | Review)
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
