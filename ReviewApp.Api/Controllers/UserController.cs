using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class UserController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly ICollectionService _collectionService;
    private readonly IReviewService _reviewService;
    private readonly IUserAuthHelper _userAuthHelper;
    private readonly ITokenService _tokenService;

    public UserController(IUserService userService, ICollectionService collectionService, IReviewService reviewService, IUserAuthHelper userAuthHelper, ITokenService tokenService)
    {
        _userService = userService;
        _collectionService = collectionService;
        _reviewService = reviewService;
        _userAuthHelper = userAuthHelper;
        _tokenService = tokenService;
    }

    [HttpGet("search")]
    public async Task<IActionResult> SearchUsers([FromQuery] string q)
    {
        var users = await _userService.SearchUsersAsync(q);
        return Ok(users);
    }

    [HttpGet("{username}")]
    public async Task<IActionResult> GetUserProfile([FromRoute] string username)
    {
        var userProfile = await _userService.GetUserProfileAsync(username, _userAuthHelper.GetOptionalUserID());
        if (userProfile == null)
            return NotFound(new { error = "User profile not found." });

        return Ok(userProfile);
    }

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

    [HttpPut("me/email")]
    [Authorize]
    [EnableRateLimiting("credential-change")]
    public async Task<IActionResult> ChangeMyEmail([FromBody] ChangeEmailDto dto)
    {
        try
        {
            var (Success, Message) = await _userService.ChangeEmailAsync(_userAuthHelper.GetSecureUserID(), dto);
            if (!Success)
                return BadRequest(new { error = Message });

            return Ok(new { message = Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

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

            // All older tokens are now revoked - give this browser a fresh one so the user stays logged in
            _tokenService.IssueAuthCookie(updatedUser!);

            return Ok(new { message = Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    [HttpGet("{username}/collections")]
    public async Task<IActionResult> GetUserCollections([FromRoute] string username, [FromQuery] string sortBy = "createdAt_asc")
    {
        var collections = await _collectionService.GetUserCollectionsAsync(username, _userAuthHelper.GetOptionalUserID(), sortBy);
        if (collections == null)
            return NotFound(new { error = "User profile not found." });

        return Ok(collections);
    }

    [HttpGet("{username}/reviews")]
    public async Task<IActionResult> GetUserReviews([FromRoute] string username, [FromQuery] ReviewFilterParams p)
    {
        var (Success, Reviews) = await _reviewService.GetUserReviewsAsync(username, _userAuthHelper.GetOptionalUserID(), p);
        if (!Success)
            return NotFound(new { error = "User profile not found." });

        return Ok(Reviews);
    }

    [HttpGet("{username}/followers")]
    public async Task<IActionResult> GetUserFollowers([FromRoute] string username)
    {
        var followers = await _userService.GetUserFollowersAsync(username, _userAuthHelper.GetOptionalUserID());
        return Ok(followers);
    }

    [HttpGet("{username}/following")]
    public async Task<IActionResult> GetUserFollowing([FromRoute] string username)
    {
        var following = await _userService.GetUserFollowingAsync(username, _userAuthHelper.GetOptionalUserID());
        return Ok(following);
    }
}
