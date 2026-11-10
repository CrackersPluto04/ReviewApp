using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Filters;
using ReviewApp.Api.Services;
using ReviewApp.Api.Services.Interfaces;
using ReviewApp.Api.Services.Payments;

namespace ReviewApp.Api.Controllers;

/// <summary>
/// The monthly Premium subscription. The local record only ever changes from what the payment provider reports, so
/// the redirects back from the provider prove nothing and the webhook is only a trigger to fetch the current
/// subscription. Actions that call the provider share the per-user <c>billing</c> rate limit.
/// </summary>
[Route("api/[controller]")]
[ApiController]
public class SubscriptionController : ControllerBase
{
    private static readonly HashSet<string> AndroidReturnResults = ["success", "cancelled", "portal"];

    private const string AndroidReturnStyle =
        "body{font-family:system-ui,sans-serif;text-align:center;padding:48px 16px;color:#1f1f1f;background:#fafafa}" +
        "a{display:inline-block;margin-top:24px;padding:12px 24px;border-radius:24px;background:#1976d2;color:#fff;text-decoration:none}";

    private static readonly string AndroidReturnStyleHash =
        "sha256-" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(AndroidReturnStyle)));

    private readonly ISubscriptionService _subscriptionService;
    private readonly IUserAuthHelper _userAuthHelper;
    private readonly IWebHostEnvironment _environment;

    public SubscriptionController(ISubscriptionService subscriptionService, IUserAuthHelper userAuthHelper, IWebHostEnvironment environment)
    {
        _subscriptionService = subscriptionService;
        _userAuthHelper = userAuthHelper;
        _environment = environment;
    }

    /// <summary>
    /// Describes the Premium plan: price, currency and the features of each tier. Anonymous, because the plans
    /// page is visible before logging in.
    /// </summary>
    /// <returns>The plan.</returns>
    /// <response code="200">The plan.</response>
    [HttpGet("plan")]
    public IActionResult GetPlan() => Ok(_subscriptionService.GetPlan());

    /// <summary>
    /// Returns the caller's subscription state from the local record: tier, status, period end and whether it
    /// renews. A caller who never started a checkout is simply Free.
    /// </summary>
    /// <returns>The subscription status.</returns>
    /// <response code="200">The status.</response>
    [HttpGet("status")]
    [Authorize]
    public async Task<IActionResult> GetStatus()
    {
        try
        {
            return Ok(await _subscriptionService.GetStatusAsync(_userAuthHelper.GetSecureUserID()));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Starts a checkout and returns the provider's payment page. The platform decides where the provider sends
    /// the user back afterwards (the web app or the Android deep-link page).
    /// </summary>
    /// <param name="dto">The platform, <c>web</c> or <c>android</c>.</param>
    /// <returns><c>{ url }</c> to redirect the user to.</returns>
    /// <response code="200">The checkout URL.</response>
    /// <response code="400">Checkout is not possible: the caller already has a running subscription, owes a failed payment (they should update their payment details instead), a checkout is already being prepared, or the provider is unavailable.</response>
    /// <response code="429">The <c>billing</c> rate limit was hit.</response>
    [HttpPost("checkout")]
    [Authorize]
    [EnableRateLimiting("billing")]
    public async Task<IActionResult> StartCheckout([FromBody] SubscriptionRedirectDto dto)
    {
        try
        {
            var (Success, Message, Url) = await _subscriptionService.StartCheckoutAsync(_userAuthHelper.GetSecureUserID(), dto.Platform == "android");
            if (!Success)
                return BadRequest(new { error = Message });

            return Ok(new SubscriptionUrlDto { Url = Url! });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Returns the provider's customer portal, where payment details and invoices are managed.
    /// </summary>
    /// <param name="dto">The platform, <c>web</c> or <c>android</c>, which decides the return URL.</param>
    /// <returns><c>{ url }</c> to redirect the user to.</returns>
    /// <response code="200">The portal URL.</response>
    /// <response code="400">The caller has no subscription to manage, or the provider is unavailable.</response>
    /// <response code="429">The <c>billing</c> rate limit was hit.</response>
    [HttpPost("portal")]
    [Authorize]
    [EnableRateLimiting("billing")]
    public async Task<IActionResult> OpenPortal([FromBody] SubscriptionRedirectDto dto)
    {
        try
        {
            var (Success, Message, Url) = await _subscriptionService.CreatePortalAsync(_userAuthHelper.GetSecureUserID(), dto.Platform == "android");
            if (!Success)
                return BadRequest(new { error = Message });

            return Ok(new SubscriptionUrlDto { Url = Url! });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Stops the subscription from renewing. Access continues until the end of the paid period.
    /// </summary>
    /// <returns>A confirmation message.</returns>
    /// <response code="200">Cancelled at period end, or it already was.</response>
    /// <response code="400">The caller has no running subscription, or the provider is unavailable.</response>
    /// <response code="429">The <c>billing</c> rate limit was hit.</response>
    [HttpPost("cancel")]
    [Authorize]
    [EnableRateLimiting("billing")]
    public Task<IActionResult> Cancel() => Run(_subscriptionService.CancelAsync);

    /// <summary>
    /// Undoes a cancellation made earlier in the same paid period, so the subscription renews again.
    /// </summary>
    /// <returns>A confirmation message.</returns>
    /// <response code="200">Renewal restored, or the subscription was not cancelled in the first place.</response>
    /// <response code="400">The subscription has already ended (subscribing again is the way back), or the provider is unavailable.</response>
    /// <response code="429">The <c>billing</c> rate limit was hit.</response>
    [HttpPost("resume")]
    [Authorize]
    [EnableRateLimiting("billing")]
    public Task<IActionResult> Resume() => Run(_subscriptionService.ResumeAsync);

    /// <summary>
    /// Fetches the caller's subscription from the provider and updates the local record. The return pages call
    /// it because the redirect itself proves nothing and the webhook may not have arrived yet.
    /// </summary>
    /// <returns>The refreshed subscription status.</returns>
    /// <response code="200">The status after syncing.</response>
    /// <response code="429">The <c>billing</c> rate limit was hit.</response>
    [HttpPost("sync")]
    [Authorize]
    [EnableRateLimiting("billing")]
    public async Task<IActionResult> Sync()
    {
        try
        {
            return Ok(await _subscriptionService.SyncAsync(_userAuthHelper.GetSecureUserID()));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Receives provider notifications (Stripe webhooks). Anonymous: authenticated by the signature over the raw
    /// body. Not rate limited, because the provider retries rejected deliveries, which would only delay them.
    /// Events are only triggers to fetch the current subscription, so handling is idempotent and order-independent.
    /// </summary>
    /// <param name="signature">The <c>Stripe-Signature</c> header.</param>
    /// <response code="200">Accepted, including event types that are ignored.</response>
    /// <response code="400">The signature is invalid.</response>
    /// <response code="500">Processing failed; the provider will redeliver the event.</response>
    [HttpPost("webhook")]
    [DisableRateLimiting]
    public async Task<IActionResult> Webhook([FromHeader(Name = "Stripe-Signature")] string? signature)
    {
        // The signature is over the exact bytes, so the body is read raw instead of model bound
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync();

        try
        {
            if (!await _subscriptionService.HandleWebhookAsync(payload, signature))
                return BadRequest(new { error = "Invalid signature." });

            return Ok();
        }
        catch (Exception)
        {
            // Already logged; a 500 makes the provider retry later
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "The event could not be processed." });
        }
    }

    /// <summary>
    /// Where the provider sends an Android user after checkout or the payment settings. It is opened in the
    /// phone's browser and hands over to the app with a <c>reviewapp://subscription/{result}</c> deep link. It
    /// changes nothing: the app syncs when it opens.
    /// </summary>
    /// <param name="result"><c>success</c>, <c>cancelled</c> or <c>portal</c>; anything else is treated as <c>cancelled</c>.</param>
    /// <returns>A small HTML page with a content security policy that allows only its own inline style block.</returns>
    /// <response code="200">The hand-over page.</response>
    [HttpGet("return/android")]
    public ContentResult AndroidReturn([FromQuery] string? result)
    {
        var safeResult = result != null && AndroidReturnResults.Contains(result) ? result : "cancelled";
        var appLink = WebUtility.HtmlEncode($"reviewapp://subscription/{safeResult}");
        var title = safeResult switch
        {
            "success" => "Thanks for subscribing!",
            "portal" => "Done with your payment settings",
            _ => "Checkout cancelled"
        };

        // Nothing but the one inline style block may run on this page
        Response.Headers.ContentSecurityPolicy = $"default-src 'none'; style-src '{AndroidReturnStyleHash}'";
        return Content($$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <meta http-equiv="refresh" content="0;url={{appLink}}">
              <title>THE Review App</title>
              <style>{{AndroidReturnStyle}}</style>
            </head>
            <body>
              <h1>{{title}}</h1>
              <p>You can go back to THE Review App now.</p>
              <a href="{{appLink}}">Return to the app</a>
            </body>
            </html>
            """, "text/html");
    }

    /// <summary>
    /// Development only: plays a provider event (payment failed, renewed, ended, lapsed) against the current
    /// user's subscription of the Fake provider.
    /// </summary>
    /// <param name="dto">The event to play.</param>
    /// <returns>A confirmation message.</returns>
    /// <response code="200">The event was applied.</response>
    /// <response code="400">The event cannot be applied, e.g. the provider is not the Fake one.</response>
    /// <response code="404">Not running in Development.</response>
    [HttpPost("dev/simulate")]
    [Authorize]
    public async Task<IActionResult> Simulate([FromBody] SimulateSubscriptionEventDto dto)
    {
        if (!_environment.IsDevelopment())
            return NotFound();

        try
        {
            var (Success, Message) = await _subscriptionService.SimulateAsync(
                _userAuthHelper.GetSecureUserID(), Enum.Parse<FakeSubscriptionEvent>(dto.Event));
            if (!Success)
                return BadRequest(new { error = Message });

            return Ok(new { message = Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    private async Task<IActionResult> Run(Func<int, Task<(bool Success, string Message)>> action)
    {
        try
        {
            var (Success, Message) = await action(_userAuthHelper.GetSecureUserID());
            if (!Success)
                return BadRequest(new { error = Message });

            return Ok(new { message = Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }
}
