using Microsoft.AspNetCore.Mvc;
using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Controllers;

/// <summary>
/// Read access to users' achievement progress. Anonymous: progress is part of the public profile.
/// </summary>
[Route("api/[controller]")]
[ApiController]
public class AchievementController : ControllerBase
{
    private readonly IAchievementService _achievementService;

    public AchievementController(IAchievementService achievementService)
    {
        _achievementService = achievementService;
    }

    /// <summary>
    /// Lists every achievement with the given user's progress and unlock state. Public, so anonymous visitors can see it.
    /// </summary>
    /// <param name="username">The profile owner's username.</param>
    /// <returns>The achievements, including those with no progress yet.</returns>
    /// <response code="200">The user's achievements.</response>
    /// <response code="404">No user has this username.</response>
    [HttpGet("{username}")]
    public async Task<IActionResult> GetUserAchievements([FromRoute] string username)
    {
        var achievements = await _achievementService.GetUserAchievementsAsync(username);
        if (achievements == null) return NotFound(new { error = "User profile not found." });

        return Ok(achievements);
    }
}
