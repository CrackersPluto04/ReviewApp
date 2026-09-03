using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class CollectionController : ControllerBase
{
    private readonly ICollectionService _collectionService;
    private readonly IUserAuthHelper _userAuthHelper;

    public CollectionController(ICollectionService collectionService, IUserAuthHelper userAuthHelper)
    {
        _collectionService = collectionService;
        _userAuthHelper = userAuthHelper;
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetCollectionWithMedias([FromRoute] int id)
    {
        var collection = await _collectionService.OpenCollectionAsync(_userAuthHelper.GetOptionalUserID(), id);
        if (collection == null) return NotFound(new { error = "Collection not found or is private." });

        return Ok(collection);
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> CreateCollection([FromBody] CreateCollectionDto dto)
    {
        try
        {
            var result = await _collectionService.CreateCollectionAsync(_userAuthHelper.GetSecureUserID(), dto);
            if (result == null) return BadRequest(new { error = "You already have a collection with this name." });

            return CreatedAtAction(nameof(GetCollectionWithMedias), new { id = result.ID }, result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    [HttpPut]
    [Authorize]
    public async Task<IActionResult> UpdateCollection([FromBody] UpdateCollectionDto dto)
    {
        try
        {
            var result = await _collectionService.UpdateCollectionAsync(_userAuthHelper.GetSecureUserID(), dto);
            if (result == null) return BadRequest(new { error = "Update failed. Name might be taken, or collection not found." });

            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    [HttpDelete("{id}")]
    [Authorize]
    public async Task<IActionResult> DeleteCollection([FromRoute] int id)
    {
        try
        {
            var success = await _collectionService.DeleteCollectionAsync(_userAuthHelper.GetSecureUserID(), id);
            if (!success) return NotFound(new { error = "Collection not found." });

            return NoContent();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    [HttpPost("{id}/media")]
    [Authorize]
    public async Task<IActionResult> AddMediaToCollection([FromRoute] int id, [FromBody] AddMediaToCollectionDto dto)
    {
        try
        {
            var success = await _collectionService.AddMediaToCollectionAsync(_userAuthHelper.GetSecureUserID(), id, dto.Type, dto.ExternalApiID);
            if (!success) return BadRequest(new { error = "Failed to add media. It may already exist in this collection." });

            return Ok(new { message = "Media added successfully." });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    [HttpDelete("{id}/media/{type}-{externalApiId}")]
    [Authorize]
    public async Task<IActionResult> RemoveMediaFromCollection([FromRoute] int id, [FromRoute] MediaType type, [FromRoute] string externalApiId)
    {
        try
        {
            var success = await _collectionService.RemoveMediaFromCollectionAsync(_userAuthHelper.GetSecureUserID(), id, type, externalApiId);
            if (!success) return NotFound(new { error = "Media not found in this collection." });

            return NoContent();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    [HttpPut("{id}/reorder")]
    [Authorize]
    public async Task<IActionResult> ReorderMedia([FromRoute] int id, [FromBody] ReorderMediaDto dto)
    {
        try
        {
            var success = await _collectionService.ReorderMediaAsync(_userAuthHelper.GetSecureUserID(), id, dto.DbMediaID, dto.NewOrderIndex);
            if (!success) return BadRequest(new { error = "Failed to reorder media." });

            return Ok(new { message = "Media reordered successfully." });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }
}
