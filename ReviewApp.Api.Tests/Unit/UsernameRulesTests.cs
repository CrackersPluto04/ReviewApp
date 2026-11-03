using ReviewApp.Api.Services;

namespace ReviewApp.Api.Tests.Unit;

// F3: usernames are part of URLs, so only characters that never need escaping, and no route words
public class UsernameRulesTests
{
    [Theory]
    [InlineData("abc")]
    [InlineData("Ben_2004")]
    [InlineData("movie-fan")]
    [InlineData("ABCDEFGHIJKLMNOPQRST")]
    [InlineData("searcher")]
    public void Valid_Names(string username)
    {
        UsernameRules.Validate(username).ShouldBeNull();
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTU")]
    public void Length_IsThreeToTwenty(string username)
    {
        UsernameRules.Validate(username).ShouldBe("Username must be between 3 and 20 characters.");
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("what?")]
    [InlineData("dot.name")]
    [InlineData("two words")]
    [InlineData("Bálint")]
    [InlineData("50%off")]
    public void Characters_AreLettersDigitsUnderscoreAndHyphen(string username)
    {
        UsernameRules.Validate(username).ShouldBe("Username can only contain letters, numbers, '_' and '-'.");
    }

    [Theory]
    [InlineData("search")]
    [InlineData("SEARCH")]
    [InlineData("me")]
    [InlineData("Admin")]
    public void RouteWords_AreReserved(string username)
    {
        // "me" is too short anyway; the length rule answers first
        UsernameRules.Validate(username).ShouldNotBeNull();
        if (username.Length >= UsernameRules.MinLength)
            UsernameRules.Validate(username).ShouldBe("This username is reserved, please choose another one.");
    }
}
