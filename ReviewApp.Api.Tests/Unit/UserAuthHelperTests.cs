using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ReviewApp.Api.Services;

namespace ReviewApp.Api.Tests.Unit;

public class UserAuthHelperTests
{
    private static UserAuthHelper HelperFor(params Claim[] claims) => new(new HttpContextAccessor
    {
        HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) }
    });

    [Fact]
    public void ValidIdClaim_IsReturnedByBoth()
    {
        var helper = HelperFor(new Claim("id", "7"));

        helper.GetSecureUserID().ShouldBe(7);
        helper.GetOptionalUserID().ShouldBe(7);
    }

    [Fact]
    public void MissingClaim_SecureThrowsOptionalIsNull()
    {
        var helper = HelperFor();

        Should.Throw<UnauthorizedAccessException>(() => helper.GetSecureUserID());
        helper.GetOptionalUserID().ShouldBeNull();
    }

    [Fact]
    public void NonNumericClaim_SecureThrowsOptionalIsNull()
    {
        var helper = HelperFor(new Claim("id", "abc"));

        Should.Throw<UnauthorizedAccessException>(() => helper.GetSecureUserID());
        helper.GetOptionalUserID().ShouldBeNull();
    }

    [Fact]
    public void NoHttpContext_SecureThrowsOptionalIsNull()
    {
        var helper = new UserAuthHelper(new HttpContextAccessor());

        Should.Throw<UnauthorizedAccessException>(() => helper.GetSecureUserID());
        helper.GetOptionalUserID().ShouldBeNull();
    }
}
