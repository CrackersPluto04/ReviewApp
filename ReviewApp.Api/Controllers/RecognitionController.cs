using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using ReviewApp.Api.Filters;
using ReviewApp.Api.Services;
using ReviewApp.Api.Services.Interfaces;
using ReviewApp.Api.Services.Recognition;

namespace ReviewApp.Api.Controllers;

/// <summary>
/// Camera recognition (Premium): identifies the title or franchise in a photo and lists related media.
/// Errors are <c>{ error, code }</c> so the clients can tell the states apart: <c>busy</c> (409),
/// <c>daily_limit</c> (429, with <c>resetsAt</c>), <c>recognition_unavailable</c> / <c>source_unavailable</c> (503)
/// and <c>not_found</c> (404).
/// </summary>
[Route("api/[controller]")]
[ApiController]
[Authorize]
public class RecognitionController : ControllerBase
{
    private readonly IRecognitionService _recognitionService;
    private readonly IUserAuthHelper _userAuthHelper;
    private readonly RecognitionOptions _options;

    public RecognitionController(IRecognitionService recognitionService, IUserAuthHelper userAuthHelper, IOptions<RecognitionOptions> options)
    {
        _recognitionService = recognitionService;
        _userAuthHelper = userAuthHelper;
        _options = options.Value;
    }

    /// <summary>
    /// Reports whether the feature is enabled, whether the caller has it, and the daily allowance left.
    /// Deliberately not Premium-locked: Free users need it to show the feature's entry point and the upsell.
    /// </summary>
    /// <returns><c>{ enabled, premium, limit, remaining, resetsAt }</c>.</returns>
    /// <response code="200">The caller's allowance.</response>
    [HttpGet("allowance")]
    public async Task<IActionResult> GetAllowance()
    {
        try
        {
            return Ok(await _recognitionService.GetAllowanceAsync(_userAuthHelper.GetSecureUserID()));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Recognizes what a photo shows and returns the first page of matching media. The request is
    /// <c>multipart/form-data</c> with the photo (JPEG or PNG) in the <c>image</c> part. The photo is read into
    /// memory and is never stored or logged.
    /// </summary>
    /// <returns>The recognition ID, the recognized subject and the first page of ranked media.</returns>
    /// <response code="200">Recognized, or nothing recognized (an empty list).</response>
    /// <response code="400">Not multipart, no photo in the <c>image</c> part, or an unusable image.</response>
    /// <response code="402">The caller is not Premium (<c>premium_required</c>).</response>
    /// <response code="409">The caller already has a recognition running (<c>busy</c>).</response>
    /// <response code="413">The photo is over the size limit.</response>
    /// <response code="429">Daily allowance used up (<c>daily_limit</c>, with <c>resetsAt</c>), or the <c>recognition</c> rate limit.</response>
    /// <response code="503">The feature is off or the AI provider failed (<c>recognition_unavailable</c>), or TMDb / Spotify were unreachable (<c>source_unavailable</c>).</response>
    [HttpPost]
    [RequiresFeature(FeatureKeys.CameraRecognition)]
    [EnableRateLimiting("recognition")]
    [RequestSizeLimit(RecognitionOptions.MaxRequestBytes)]
    public async Task<IActionResult> Recognize()
    {
        try
        {
            var userId = _userAuthHelper.GetSecureUserID();
            if (!_recognitionService.IsEnabled)
                return Unavailable("Camera recognition is temporarily unavailable.", "recognition_unavailable");

            var upload = await MultipartImageReader.ReadAsync(Request, _options.MaxImageBytes, HttpContext.RequestAborted);
            switch (upload.Status)
            {
                case ImageReadStatus.TooLarge:
                    return StatusCode(StatusCodes.Status413PayloadTooLarge,
                        new { error = $"The photo is too large (max {_options.MaxImageBytes / (1024 * 1024)} MB)." });
                case ImageReadStatus.NotMultipart:
                    return BadRequest(new { error = "Send the photo as multipart/form-data." });
                case ImageReadStatus.Missing:
                    return BadRequest(new { error = $"No photo was sent (expected a file in the \"{MultipartImageReader.PartName}\" field)." });
            }

            var result = await _recognitionService.RecognizeAsync(userId, upload.Data!);
            return result.Status switch
            {
                RecognitionStatus.Ok => Ok(result.Response),
                RecognitionStatus.BadImage => BadRequest(new { error = result.Error }),
                RecognitionStatus.TooLarge => StatusCode(StatusCodes.Status413PayloadTooLarge, new { error = result.Error }),
                RecognitionStatus.Busy => Conflict(new { error = result.Error, code = "busy" }),
                RecognitionStatus.DailyLimit => StatusCode(StatusCodes.Status429TooManyRequests,
                    new { error = result.Error, code = "daily_limit", resetsAt = result.ResetsAt }),
                RecognitionStatus.SourceUnavailable => Unavailable(result.Error!, "source_unavailable"),
                _ => Unavailable(result.Error!, "recognition_unavailable")
            };
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    /// <summary>
    /// "Show more": returns further items of a stored recognition result without a new AI call or allowance use.
    /// Premium-locked too, so a lapsed subscription stops here as well.
    /// </summary>
    /// <param name="id">The recognition ID from the original response.</param>
    /// <param name="offset">Index of the first item to return.</param>
    /// <param name="count">How many items to return; the configured page size when omitted.</param>
    /// <returns>The requested slice of the stored list.</returns>
    /// <response code="200">The items.</response>
    /// <response code="402">The caller is not Premium (<c>premium_required</c>).</response>
    /// <response code="404">The ID is unknown, expired (results are kept 30 minutes) or belongs to someone else (<c>not_found</c>).</response>
    [HttpGet("{id}/items")]
    [RequiresFeature(FeatureKeys.CameraRecognition)]
    public IActionResult GetItems([FromRoute] string id, [FromQuery] int offset = 0, [FromQuery] int? count = null)
    {
        try
        {
            var items = _recognitionService.GetItems(_userAuthHelper.GetSecureUserID(), id, offset, count ?? _options.PageSize);
            return items == null
                ? NotFound(new { error = "This recognition has expired. Take a new photo to try again.", code = "not_found" })
                : Ok(items);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    private ObjectResult Unavailable(string error, string code) =>
        StatusCode(StatusCodes.Status503ServiceUnavailable, new { error, code });
}
