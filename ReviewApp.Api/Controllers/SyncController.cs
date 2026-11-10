using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Services.Interfaces;
using ReviewApp.Api.Services.Sync;

namespace ReviewApp.Api.Controllers;

/// <summary>
/// The Android app's offline sync: the device pushes its queued changes and pulls what changed elsewhere.
/// Deliberately not Premium-gated: changes made offline must still arrive after Premium lapses. Only working
/// offline is the Premium feature, and that is decided on the device. All endpoints share the per-user
/// <c>sync</c> rate limit (a token bucket).
/// </summary>
[Route("api/[controller]")]
[ApiController]
[Authorize]
public class SyncController : ControllerBase
{
    /// <summary>Largest accepted push request body, in bytes.</summary>
    public const int MaxPushBytes = 512 * 1024;

    private readonly ISyncService _syncService;
    private readonly ISyncChangesService _changesService;
    private readonly IUserAuthHelper _userAuthHelper;

    public SyncController(ISyncService syncService, ISyncChangesService changesService, IUserAuthHelper userAuthHelper)
    {
        _syncService = syncService;
        _changesService = changesService;
        _userAuthHelper = userAuthHelper;
    }

    /// <summary>
    /// Applies the device's queued changes ("ops") in order. Each op is applied and answered on its own, so one
    /// failure never fails the request. An op that was already applied (same <c>opId</c>) is answered from its
    /// stored result instead of being applied twice.
    /// </summary>
    /// <param name="request">The device's user and its ops, at most 50.</param>
    /// <param name="cancellationToken">Cancelled when the client disconnects.</param>
    /// <returns>One result per op (applied, merged, conflict, rejected, retry or skipped), the achievements the ops unlocked, and the server time.</returns>
    /// <response code="200">Every op was processed, whatever its individual outcome.</response>
    /// <response code="400">Too many ops (<c>too_many_ops</c>) or a repeated op ID (<c>duplicate_op</c>).</response>
    /// <response code="409">The queue belongs to another account than the logged-in one (<c>user_mismatch</c>); nothing is applied.</response>
    /// <response code="413">The body is over 512 KB.</response>
    /// <response code="429">The <c>sync</c> rate limit was hit.</response>
    [HttpPost("push")]
    [EnableRateLimiting("sync")]
    [RequestSizeLimit(MaxPushBytes)]
    public async Task<IActionResult> Push([FromBody] SyncPushRequest request, CancellationToken cancellationToken)
    {
        int userId;
        try
        {
            userId = _userAuthHelper.GetSecureUserID();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }

        // The queue belongs to another account than the logged-in one: never applied under this one
        if (request.UserId != userId)
            return Conflict(new { error = "These changes belong to another account.", code = "user_mismatch" });

        var ops = request.Ops!;
        if (ops.Count > SyncService.MaxOps)
            return BadRequest(new { error = $"At most {SyncService.MaxOps} changes can be sent at once.", code = "too_many_ops" });
        if (ops.Any(op => op == null) || ops.Select(op => op.OpId).Distinct().Count() != ops.Count)
            return BadRequest(new { error = "Every change needs its own ID.", code = "duplicate_op" });

        return Ok(await _syncService.PushAsync(userId, ops, cancellationToken));
    }

    /// <summary>
    /// Pulls the caller's own reviews and collections (every visibility) that changed since a cursor, plus
    /// tombstones of deleted ones, oldest change first.
    /// </summary>
    /// <param name="since">The cursor from the previous page; 0 means everything.</param>
    /// <param name="syncedAt">When the device last finished a sync. If that was longer ago than the tombstone retention, the answer is a full resync because deletions may have been forgotten.</param>
    /// <param name="continued"><c>true</c> for a follow-up page of the same run: no account data and no full-resync check.</param>
    /// <param name="limit">Page size.</param>
    /// <param name="cancellationToken">Cancelled when the client disconnects.</param>
    /// <returns>The changes, the next cursor and, on the first page, the account data.</returns>
    /// <response code="200">The page of changes.</response>
    /// <response code="429">The <c>sync</c> rate limit was hit.</response>
    [HttpGet("changes")]
    [EnableRateLimiting("sync")]
    public async Task<IActionResult> Changes([FromQuery] long since = 0, [FromQuery] DateTime? syncedAt = null,
        [FromQuery] bool continued = false, [FromQuery] int limit = SyncChangesService.DefaultLimit, CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = _userAuthHelper.GetSecureUserID();
            return Ok(await _changesService.GetChangesAsync(userId, since, syncedAt, continued, limit, cancellationToken));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Answers which of the other users' reviews and collections cached on the device are still visible to the
    /// caller (public, or followers-only while following), so the device can prune the rest.
    /// </summary>
    /// <param name="request">The cached review and collection IDs, at most 1000 of each.</param>
    /// <param name="cancellationToken">Cancelled when the client disconnects.</param>
    /// <returns>The IDs that are still visible.</returns>
    /// <response code="200">The visible IDs.</response>
    /// <response code="429">The <c>sync</c> rate limit was hit.</response>
    [HttpPost("visible")]
    [EnableRateLimiting("sync")]
    public async Task<IActionResult> Visible([FromBody] SyncVisibleRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var userId = _userAuthHelper.GetSecureUserID();
            return Ok(await _changesService.GetVisibleAsync(userId, request.ReviewIds ?? [], request.CollectionIds ?? [], cancellationToken));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }
}
