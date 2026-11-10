using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Controllers;

/// <summary>
/// Collections of media. Reading one is open to anonymous visitors (private collections answer 404); every change
/// needs a login and only works on the caller's own collections. The default Favourites collection is permanent and
/// cannot be renamed, edited or deleted.
/// </summary>
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

    /// <summary>
    /// Lists every title in each of the caller's collections. Clients cache it to show favourite and collection
    /// state on media cards without one request per card.
    /// </summary>
    /// <returns>One entry per collection and media: collection ID, media type and external API ID.</returns>
    /// <response code="200">The caller's memberships.</response>
    [HttpGet("me/memberships")]
    [Authorize]
    public async Task<IActionResult> GetMyMemberships()
    {
        try
        {
            return Ok(await _collectionService.GetMembershipsAsync(_userAuthHelper.GetSecureUserID()));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Opens a collection with its media. The caller is optional: visibility is decided per viewer.
    /// </summary>
    /// <param name="id">The collection's ID.</param>
    /// <returns>The collection with its media in the saved order.</returns>
    /// <response code="200">The collection.</response>
    /// <response code="404">The collection does not exist or the viewer may not see it. Both look the same on purpose.</response>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetCollectionWithMedias([FromRoute] int id)
    {
        var collection = await _collectionService.OpenCollectionAsync(_userAuthHelper.GetOptionalUserID(), id);
        if (collection == null) return NotFound(new { error = "Collection not found or is private." });

        return Ok(collection);
    }

    /// <summary>
    /// Creates a collection for the caller. Names are unique per user.
    /// </summary>
    /// <param name="dto">The new collection's name and settings.</param>
    /// <returns>The created collection.</returns>
    /// <response code="201">Created; the Location header points to the collection.</response>
    /// <response code="400">The caller already has a collection with this name.</response>
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

    /// <summary>
    /// Changes a collection's name or settings.
    /// </summary>
    /// <param name="dto">The collection's ID and its new values.</param>
    /// <returns>The updated collection.</returns>
    /// <response code="200">Updated.</response>
    /// <response code="400">Empty or taken name, or the collection is the permanent Favourites.</response>
    /// <response code="404">The caller has no such collection.</response>
    [HttpPut]
    [Authorize]
    public async Task<IActionResult> UpdateCollection([FromBody] UpdateCollectionDto dto)
    {
        try
        {
            var result = await _collectionService.UpdateCollectionAsync(_userAuthHelper.GetSecureUserID(), dto);
            if (result.Success)
                return Ok(result.Collection);
            else if (result.NotFound)
                return NotFound(new { error = result.Message });
            else
                return BadRequest(new { error = result.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Deletes one of the caller's collections together with its item links. The media themselves stay.
    /// </summary>
    /// <param name="id">The collection's ID.</param>
    /// <response code="204">Deleted.</response>
    /// <response code="400">The collection is the permanent Favourites.</response>
    /// <response code="404">The caller has no such collection.</response>
    [HttpDelete("{id}")]
    [Authorize]
    public async Task<IActionResult> DeleteCollection([FromRoute] int id)
    {
        try
        {
            var result = await _collectionService.DeleteCollectionAsync(_userAuthHelper.GetSecureUserID(), id);
            if (result.Success)
                return NoContent();
            else if (result.NotFound)
                return NotFound(new { error = result.Message });
            else
                return BadRequest(new { error = result.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Adds a title to one of the caller's collections. The title is fetched from TMDb / Spotify and cached
    /// locally the first time anyone uses it.
    /// </summary>
    /// <param name="id">The collection's ID.</param>
    /// <param name="dto">The media type and its external API ID.</param>
    /// <response code="200">Added.</response>
    /// <response code="400">Not added, e.g. it is already in the collection or the collection is not the caller's.</response>
    /// <response code="404">The source does not know this title (<c>media_not_found</c>).</response>
    /// <response code="503">TMDb / Spotify could not be reached (<c>source_unavailable</c>).</response>
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

    /// <summary>
    /// Removes a title from one of the caller's collections.
    /// </summary>
    /// <param name="id">The collection's ID.</param>
    /// <param name="type">The media type, the part of the route before the dash.</param>
    /// <param name="externalApiId">The title's external API ID, the part after the first dash.</param>
    /// <response code="204">Removed.</response>
    /// <response code="404">The title is not in this collection.</response>
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

    /// <summary>
    /// Moves one title to a new position in the collection's order.
    /// </summary>
    /// <param name="id">The collection's ID.</param>
    /// <param name="dto">The stored media ID and its new index.</param>
    /// <response code="200">Reordered.</response>
    /// <response code="400">The move failed, e.g. the title is not in the collection.</response>
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
