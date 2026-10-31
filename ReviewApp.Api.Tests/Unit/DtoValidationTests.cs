using System.ComponentModel.DataAnnotations;
using System.Text;
using ReviewApp.Api.DTOs;

namespace ReviewApp.Api.Tests.Unit;

// The request rules live on the DTOs ([ApiController] rejects invalid ones with a 400), the services trust them
public class DtoValidationTests
{
    private static List<string> Errors(object dto)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true);
        return results.SelectMany(r => r.MemberNames).ToList();
    }

    [Fact]
    public void Register_ValidInput_Passes()
    {
        Errors(new UserRegisterDto { Username = "alice", Email = "alice@test.local", Password = "password1" }).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("al", "alice@test.local", "password1", "Username")]
    [InlineData("abcdefghijklmnopqrstu", "alice@test.local", "password1", "Username")]
    [InlineData("alice", "not-an-email", "password1", "Email")]
    [InlineData("alice", "alice@test.local", "short", "Password")]
    [InlineData("alice", "alice@test.local", "abcdefghijklmnopqrstu", "Password")]
    [InlineData("", "alice@test.local", "password1", "Username")]
    public void Register_InvalidInput_FailsOnTheField(string username, string email, string password, string field)
    {
        Errors(new UserRegisterDto { Username = username, Email = email, Password = password }).ShouldContain(field);
    }

    [Fact]
    public void Login_TooLongPassword_IsRejectedBeforeBcrypt()
    {
        Errors(new UserLoginDto { Email = "a@test.local", Password = new string('x', 21) }).ShouldContain("Password");
        Errors(new UserLoginDto { Email = "a@test.local", Password = "x" }).ShouldBeEmpty();
    }

    [Fact]
    public void PasswordCap_KeepsEveryPasswordUnderBcryptsByteLimit()
    {
        // UTF-16 units are counted: a BMP character is at most 3 UTF-8 bytes, a surrogate pair 4 bytes for 2 units
        var worstBmp = new string('€', 20); // the euro sign, 3 bytes each
        var worstAstral = string.Concat(Enumerable.Repeat("\U0001F600", 10)); // 20 units, 40 bytes

        Errors(new ChangePasswordDto { CurrentPassword = "x", NewPassword = worstBmp }).ShouldBeEmpty();
        Encoding.UTF8.GetByteCount(worstBmp).ShouldBeLessThanOrEqualTo(72);
        Encoding.UTF8.GetByteCount(worstAstral).ShouldBeLessThanOrEqualTo(72);
    }

    [Fact]
    public void ChangePassword_NewPasswordNeedsEightToTwentyCharacters()
    {
        Errors(new ChangePasswordDto { CurrentPassword = "old", NewPassword = "1234567" }).ShouldContain("NewPassword");
        Errors(new ChangePasswordDto { CurrentPassword = "old", NewPassword = "12345678" }).ShouldBeEmpty();
        Errors(new ChangePasswordDto { CurrentPassword = "", NewPassword = "12345678" }).ShouldContain("CurrentPassword");
    }

    [Fact]
    public void ChangeEmail_NeedsValidEmailAndPassword()
    {
        Errors(new ChangeEmailDto { NewEmail = "bad", CurrentPassword = "pw" }).ShouldContain("NewEmail");
        Errors(new ChangeEmailDto { NewEmail = "a@test.local", CurrentPassword = "" }).ShouldContain("CurrentPassword");
        Errors(new ChangeEmailDto { NewEmail = new string('a', 250) + "@t.io", CurrentPassword = "pw" }).ShouldContain("NewEmail");
    }

    [Fact]
    public void UserUpdate_AllFieldsOptionalButBounded()
    {
        Errors(new UserUpdateDto()).ShouldBeEmpty();
        Errors(new UserUpdateDto { Username = "ab" }).ShouldContain("Username");
        Errors(new UserUpdateDto { Bio = new string('b', 151) }).ShouldContain("Bio");
        Errors(new UserUpdateDto { ProfilePictureUrl = "not a url" }).ShouldContain("ProfilePictureUrl");
        Errors(new UserUpdateDto { ProfilePictureUrl = "https://img.test/a.png" }).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("123456", true)]
    [InlineData("012345", true)]
    [InlineData("12345", false)]
    [InlineData("1234567", false)]
    [InlineData("12345a", false)]
    [InlineData(" 123456", false)]
    public void TwoFactorCode_IsExactlySixDigits(string code, bool valid)
    {
        Errors(new TwoFactorCodeDto { Code = code }).Count.ShouldBe(valid ? 0 : 1);
        Errors(new TwoFactorLoginDto { ChallengeToken = "challenge", Code = code }).Count.ShouldBe(valid ? 0 : 1);
    }

    [Fact]
    public void EmailLinkToken_IsRequiredAndBounded()
    {
        Errors(new EmailLinkTokenDto { Token = "" }).ShouldContain("Token");
        Errors(new EmailLinkTokenDto { Token = new string('t', 101) }).ShouldContain("Token");
        Errors(new EmailLinkTokenDto { Token = new string('t', 43) }).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0.9, false)]
    [InlineData(1.0, true)]
    [InlineData(10.0, true)]
    [InlineData(10.1, false)]
    public void Review_ScoreIsBetweenOneAndTen(double score, bool valid)
    {
        Errors(new ReviewDto { Score = (decimal)score }).Contains("Score").ShouldBe(!valid);
    }

    [Fact]
    public void Review_TextLimits()
    {
        Errors(new ReviewDto { Score = 5, ReviewText = new string('r', 501) }).ShouldContain("ReviewText");
        Errors(new ReviewDto { Score = 5, Pros = new string('p', 251) }).ShouldContain("Pros");
        Errors(new ReviewDto { Score = 5, Cons = new string('c', 251) }).ShouldContain("Cons");
        Errors(new ReviewDto { Score = 5, ReviewText = new string('r', 500), Pros = new string('p', 250) }).ShouldBeEmpty();
    }

    [Fact]
    public void Reply_ContentIsRequiredAndBounded()
    {
        Errors(new CreateReplyDto { ReviewID = 1, Content = "" }).ShouldContain("Content");
        Errors(new CreateReplyDto { ReviewID = 1, Content = new string('c', 501) }).ShouldContain("Content");
        Errors(new CreateReplyDto { ReviewID = 1, Content = "ok" }).ShouldBeEmpty();
    }

    [Fact]
    public void Collection_NameIsRequiredOnCreateAndBounded()
    {
        Errors(new CreateCollectionDto { Name = "" }).ShouldContain("Name");
        Errors(new CreateCollectionDto { Name = new string('n', 51) }).ShouldContain("Name");
        Errors(new UpdateCollectionDto { CollectionID = 1, Name = new string('n', 51) }).ShouldContain("Name");
    }

    [Theory]
    [InlineData("web", true)]
    [InlineData("android", true)]
    [InlineData("ios", false)]
    [InlineData("WEB", false)]
    public void SubscriptionRedirect_PlatformIsWebOrAndroid(string platform, bool valid)
    {
        Errors(new SubscriptionRedirectDto { Platform = platform }).Count.ShouldBe(valid ? 0 : 1);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(50, true)]
    [InlineData(51, false)]
    public void NotificationQuery_PageSizeIsBounded(int pageSize, bool valid)
    {
        Errors(new NotificationQueryParams { PageSize = pageSize }).Count.ShouldBe(valid ? 0 : 1);
    }
}
