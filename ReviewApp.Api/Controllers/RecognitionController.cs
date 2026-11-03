using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using ReviewApp.Api.Filters;
using ReviewApp.Api.Services;
using ReviewApp.Api.Services.Interfaces;
using ReviewApp.Api.Services.Recognition;

namespace ReviewApp.Api.Controllers;

// Camera recognition (Premium). Errors are { error, code } so the clients can tell the states apart:
// busy (409), daily_limit (429, with resetsAt), recognition_unavailable / source_unavailable (503), not_found (404).
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

    // Not Premium-locked: Free users learn whether the feature exists (to show its entry and the upsell)
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

    // multipart/form-data with the photo in the "image" part (JPEG or PNG)
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

    // "Show more": the next items of the stored list, no new AI call. Locked as well, so a lapsed subscription stops here too.
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
