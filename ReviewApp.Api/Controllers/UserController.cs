using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Controllers;

/// <summary>
/// Public user profiles and the logged-in user's own account changes. Profile data is anonymous but filtered per
/// viewer. Credential changes (email, password) need the current password and the per-user
/// <c>credential-change</c> rate limit.
/// </summary>
[Route("api/[controller]")]
[ApiController]
public class UserController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly ICollectionService _collectionService;
    private readonly IReviewService _reviewService;
    private readonly IUserAuthHelper _userAuthHelper;
    private readonly ITokenService _tokenService;
    private readonly ISecurityService _securityService;

    public UserController(IUserService userService, ICollectionService collectionService, IReviewService reviewService, IUserAuthHelper userAuthHelper, ITokenService tokenService, ISecurityService securityService)
    {
        _userService = userService;
        _collectionService = collectionService;
        _reviewService = reviewService;
        _userAuthHelper = userAuthHelper;
        _tokenService = tokenService;
        _securityService = securityService;
    }

    /// <summary>
    /// Searches users by username.
    /// </summary>
    /// <param name="q">The search text.</param>
    /// <returns>Matching users in compact form.</returns>
    /// <response code="200">The matches.</response>
    [HttpGet("search")]
    public async Task<IActionResult> SearchUsers([FromQuery] string q)
    {
        var users = await _userService.SearchUsersAsync(q);
        return Ok(users);
    }

    /// <summary>
    /// Returns a user's public profile. Usernames are part of profile URLs, which is why they are the key here
    /// rather than the ID.
    /// </summary>
    /// <param name="username">The profile owner's username.</param>
    /// <returns>The profile, including follower counts, the viewer's follow state and the Premium badge.</returns>
    /// <response code="200">The profile.</response>
    /// <response code="404">No user has this username.</response>
    [HttpGet("{username}")]
    public async Task<IActionResult> GetUserProfile([FromRoute] string username)
    {
        var userProfile = await _userService.GetUserProfileAsync(username, _userAuthHelper.GetOptionalUserID());
        if (userProfile == null)
            return NotFound(new { error = "User profile not found." });

        return Ok(userProfile);
    }

    /// <summary>
    /// Updates the caller's own profile (bio, picture, username).
    /// </summary>
    /// <param name="dto">The fields to change.</param>
    /// <returns>A confirmation message.</returns>
    /// <response code="200">Updated.</response>
    /// <response code="400">A value is invalid, e.g. a username that breaks the username rules or is taken.</response>
    [HttpPatch("me")]
    [Authorize]
    public async Task<IActionResult> UpdateMyProfile([FromBody] UserUpdateDto dto)
    {
        try
        {
            var (Success, Message) = await _userService.UpdateUserProfileAsync(_userAuthHelper.GetSecureUserID(), dto);
            if (!Success)
                return BadRequest(new { error = Message });

            return Ok(new { message = Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Requests an email change. The email is not changed yet: a confirmation link goes to the new address and a
    /// notice to the old one. See <see cref="SecurityController.ConfirmEmailChange"/> for the confirming step.
    /// </summary>
    /// <param name="dto">The new email and the current password.</param>
    /// <returns>A confirmation message.</returns>
    /// <response code="200">The confirmation link was sent.</response>
    /// <response code="400">Wrong password, unverified current email, or an invalid or taken new address.</response>
    /// <response code="429">The <c>credential-change</c> rate limit was hit.</response>
    [HttpPut("me/email")]
    [Authorize]
    [EnableRateLimiting("credential-change")]
    public async Task<IActionResult> ChangeMyEmail([FromBody] ChangeEmailDto dto)
    {
        try
        {
            var (Success, Message) = await _securityService.RequestEmailChangeAsync(_userAuthHelper.GetSecureUserID(), dto);
            if (!Success)
                return BadRequest(new { error = Message });

            return Ok(new { message = Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Changes the caller's password and revokes every other session.
    /// </summary>
    /// <param name="dto">The current and the new password.</param>
    /// <returns>A confirmation message; a fresh session cookie for this browser is set.</returns>
    /// <response code="200">Changed.</response>
    /// <response code="400">Wrong current password or an invalid new one.</response>
    /// <response code="429">The <c>credential-change</c> rate limit was hit.</response>
    [HttpPut("me/password")]
    [Authorize]
    [EnableRateLimiting("credential-change")]
    public async Task<IActionResult> ChangeMyPassword([FromBody] ChangePasswordDto dto)
    {
        try
        {
            var (Success, Message, updatedUser) = await _userService.ChangePasswordAsync(_userAuthHelper.GetSecureUserID(), dto);
            if (!Success)
                return BadRequest(new { error = Message });

            // Every older token is revoked by the password change; re-issue one so this browser stays logged in
            _tokenService.IssueAuthCookie(updatedUser!);

            return Ok(new { message = Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Lists a user's collections that the viewer may see.
    /// </summary>
    /// <param name="username">The profile owner's username.</param>
    /// <param name="sortBy">Sort key such as <c>createdAt_asc</c> (the default).</param>
    /// <returns>The collections.</returns>
    /// <response code="200">The collections.</response>
    /// <response code="404">No user has this username.</response>
    [HttpGet("{username}/collections")]
    public async Task<IActionResult> GetUserCollections([FromRoute] string username, [FromQuery] string sortBy = "createdAt_asc")
    {
        var collections = await _collectionService.GetUserCollectionsAsync(username, _userAuthHelper.GetOptionalUserID(), sortBy);
        if (collections == null)
            return NotFound(new { error = "User profile not found." });

        return Ok(collections);
    }

    /// <summary>
    /// Lists a user's reviews that the viewer may see, filtered and paged. The owner sees all of their own.
    /// </summary>
    /// <param name="username">The profile owner's username.</param>
    /// <param name="p">Filter, sort and paging parameters, from the query string.</param>
    /// <returns>A page of reviews.</returns>
    /// <response code="200">The page.</response>
    /// <response code="404">No user has this username.</response>
    [HttpGet("{username}/reviews")]
    public async Task<IActionResult> GetUserReviews([FromRoute] string username, [FromQuery] ReviewFilterParams p)
    {
        var (Success, Reviews) = await _reviewService.GetUserReviewsAsync(username, _userAuthHelper.GetOptionalUserID(), p);
        if (!Success)
            return NotFound(new { error = "User profile not found." });

        return Ok(Reviews);
    }

    /// <summary>
    /// Lists the users who follow the given user.
    /// </summary>
    /// <param name="username">The profile owner's username.</param>
    /// <returns>The followers in compact form.</returns>
    /// <response code="200">The followers.</response>
    /// <response code="404">No user has this username.</response>
    [HttpGet("{username}/followers")]
    public async Task<IActionResult> GetUserFollowers([FromRoute] string username)
    {
        var followers = await _userService.GetUserFollowersAsync(username, _userAuthHelper.GetOptionalUserID());
        if (followers == null)
            return NotFound(new { error = "User profile not found." });

        return Ok(followers);
    }

    /// <summary>
    /// Lists the users the given user follows.
    /// </summary>
    /// <param name="username">The profile owner's username.</param>
    /// <returns>The followed users in compact form.</returns>
    /// <response code="200">The followed users.</response>
    /// <response code="404">No user has this username.</response>
    [HttpGet("{username}/following")]
    public async Task<IActionResult> GetUserFollowing([FromRoute] string username)
    {
        var following = await _userService.GetUserFollowingAsync(username, _userAuthHelper.GetOptionalUserID());
        if (following == null)
            return NotFound(new { error = "User profile not found." });

        return Ok(following);
    }
}
