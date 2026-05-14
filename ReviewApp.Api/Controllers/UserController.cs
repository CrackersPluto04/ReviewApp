using Microsoft.AspNetCore.Mvc;
using ReviewApp.Api.Services;

namespace ReviewApp.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class UserController : ControllerBase
{
    private readonly ICollectionService _collectionService;

    public UserController(ICollectionService collectionService)
    {
        _collectionService = collectionService;
    }

    [HttpGet("{username}/collections")]
    public async Task<IActionResult> GetUserCollections([FromRoute] string username, [FromQuery] string sortBy = "createdAt_desc")
    {
        var collections = await _collectionService.GetUserCollectionsAsync(username, GetOptionalUserId(), sortBy);
        if (collections == null)
            return NotFound(new { error = "User profile not found." });

        return Ok(collections);
    }

    /* Helper methods */

    // Helper method to extract user ID from JWT claims, but returns null if not found or invalid
    private int? GetOptionalUserId()
    {
        var userIdClaim = User.Claims.FirstOrDefault(c => c.Type == "id");
        if (userIdClaim != null && int.TryParse(userIdClaim.Value, out int userId))
        {
            return userId;
        }
        return null;
    }
}
