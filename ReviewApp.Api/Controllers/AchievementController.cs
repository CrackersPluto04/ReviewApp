using Microsoft.AspNetCore.Mvc;
using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AchievementController : ControllerBase
{
    private readonly IAchievementService _achievementService;

    public AchievementController(IAchievementService achievementService)
    {
        _achievementService = achievementService;
    }

    // Achievement progress is public, anonymous visitors can see it too
    [HttpGet("{username}")]
    public async Task<IActionResult> GetUserAchievements([FromRoute] string username)
    {
        var achievements = await _achievementService.GetUserAchievementsAsync(username);
        if (achievements == null) return NotFound(new { error = "User profile not found." });

        return Ok(achievements);
    }
}
