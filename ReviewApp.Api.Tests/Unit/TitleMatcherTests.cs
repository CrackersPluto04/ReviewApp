using ReviewApp.Api.Services.Recognition;

namespace ReviewApp.Api.Tests.Unit;

public class TitleMatcherTests
{
    [Theory]
    [InlineData("The Avengers", "avengers")]
    [InlineData("Avengers: Age of Ultron", "avengers age of ultron")]
    [InlineData("Amélie", "amelie")]
    [InlineData("Fast & Furious", "fast and furious")]
    [InlineData("Schindler's List", "schindlers list")]
    [InlineData("  Spider-Man:   Homecoming ", "spider man homecoming")]
    [InlineData("AC/DC", "ac dc")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Normalize(string? title, string expected)
    {
        TitleMatcher.Normalize(title).ShouldBe(expected);
    }

    [Theory]
    [InlineData("Avengers", "The Avengers", true)]
    [InlineData("Iron Man", "iron man", true)]
    [InlineData("Iron Man", "Iron Man 2", false)]
    [InlineData("Star Wars", "Star Wars: Episode IV - A New Hope", false)]
    [InlineData("", "", false)]
    public void TitleMatches(string wanted, string candidate, bool expected)
    {
        TitleMatcher.TitleMatches(wanted, candidate).ShouldBe(expected);
    }

    [Theory]
    [InlineData("Star Wars: Episode IV - A New Hope", "Star Wars", true)]
    [InlineData("Star Wars: Episode VII - The Force Awakens", "Star Wars: The Force Awakens", true)]
    [InlineData("Star Wars: Episode VI - Return of the Jedi", "Return of the Jedi", true)]
    [InlineData("Minecraft", "A Minecraft Movie", true)]
    [InlineData("Indiana Jones and the Raiders of the Lost Ark", "Raiders of the Lost Ark", true)]
    [InlineData("Return of the Jedi", "Star Wars: Episode VI - Return of the Jedi", true)]
    // Same words in another order, other words, or the same title (that is TitleMatches)
    [InlineData("Wars Star", "Star Wars: Episode IV - A New Hope", false)]
    [InlineData("Star Trek", "Star Wars: Episode IV - A New Hope", false)]
    [InlineData("Iron Man", "Iron Man", false)]
    [InlineData("Iron Man", "", false)]
    [InlineData(null, "Iron Man", false)]
    public void TitleContains_MatchesLongerAndShorterForms(string? wanted, string candidate, bool expected)
    {
        TitleMatcher.TitleContains(wanted, candidate).ShouldBe(expected);
    }

    [Theory]
    [InlineData(2008, "2008-05-02", true)]
    [InlineData(2008, "2009-01-01", true)]
    [InlineData(2008, "2007", true)]
    [InlineData(2008, "2010-05-07", false)]
    [InlineData(null, "1999-01-01", true)]
    [InlineData(2008, null, true)]
    [InlineData(2008, "", true)]
    public void YearMatches_AllowsOneYearEitherWay(int? wanted, string? releaseDate, bool expected)
    {
        TitleMatcher.YearMatches(wanted, releaseDate).ShouldBe(expected);
    }
}
