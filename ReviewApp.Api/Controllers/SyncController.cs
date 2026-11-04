using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Services.Interfaces;
using ReviewApp.Api.Services.Sync;

namespace ReviewApp.Api.Controllers;

// The Android app's offline sync. Not Premium-gated: changes made offline are still delivered after Premium
// lapses; only working offline is the Premium feature (that is decided on the device).
[Route("api/[controller]")]
[ApiController]
[Authorize]
public class SyncController : ControllerBase
{
    public const int MaxPushBytes = 512 * 1024;

    private readonly ISyncService _syncService;
    private readonly IUserAuthHelper _userAuthHelper;

    public SyncController(ISyncService syncService, IUserAuthHelper userAuthHelper)
    {
        _syncService = syncService;
        _userAuthHelper = userAuthHelper;
    }

    // The device's queued changes, in order: 200 with one result per op (applied / merged / conflict / rejected /
    // retry / skipped), the achievements they unlocked and the server time
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
}
