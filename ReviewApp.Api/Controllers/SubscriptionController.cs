using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Services.Interfaces;
using ReviewApp.Api.Services.Payments;

namespace ReviewApp.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class SubscriptionController : ControllerBase
{
    private static readonly HashSet<string> AndroidReturnResults = ["success", "cancelled", "portal"];

    private readonly ISubscriptionService _subscriptionService;
    private readonly IUserAuthHelper _userAuthHelper;
    private readonly IWebHostEnvironment _environment;

    public SubscriptionController(ISubscriptionService subscriptionService, IUserAuthHelper userAuthHelper, IWebHostEnvironment environment)
    {
        _subscriptionService = subscriptionService;
        _userAuthHelper = userAuthHelper;
        _environment = environment;
    }

    // Anonymous: the plans page is visible before logging in
    [HttpGet("plan")]
    public IActionResult GetPlan() => Ok(_subscriptionService.GetPlan());

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

    // The provider's page for payment details and invoices
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

    [HttpPost("cancel")]
    [Authorize]
    [EnableRateLimiting("billing")]
    public Task<IActionResult> Cancel() => Run(_subscriptionService.CancelAsync);

    [HttpPost("resume")]
    [Authorize]
    [EnableRateLimiting("billing")]
    public Task<IActionResult> Resume() => Run(_subscriptionService.ResumeAsync);

    // Called by the return pages: the redirect itself proves nothing, the backend asks the provider
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

    // Provider notifications (Stripe webhooks). Anonymous, authenticated by the signature over the raw body.
    // Not rate limited: the provider retries rejected deliveries, which would only delay them.
    [HttpPost("webhook")]
    [DisableRateLimiting]
    public async Task<IActionResult> Webhook()
    {
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync();

        try
        {
            if (!await _subscriptionService.HandleWebhookAsync(payload, Request.Headers["Stripe-Signature"]))
                return BadRequest(new { error = "Invalid signature." });

            return Ok();
        }
        catch (Exception)
        {
            // Already logged; a 500 makes the provider retry later
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "The event could not be processed." });
        }
    }

    // Where the provider sends the Android user after checkout / the payment settings. Opened in the phone's browser,
    // so it hands over to the app with a deep link. It doesn't change anything: the app syncs when it opens.
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

        Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'";
        return Content($$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <meta http-equiv="refresh" content="0;url={{appLink}}">
              <title>THE Review App</title>
              <style>
                body { font-family: system-ui, sans-serif; text-align: center; padding: 48px 16px; color: #1f1f1f; background: #fafafa; }
                a { display: inline-block; margin-top: 24px; padding: 12px 24px; border-radius: 24px; background: #1976d2; color: #fff; text-decoration: none; }
              </style>
            </head>
            <body>
              <h1>{{title}}</h1>
              <p>You can go back to THE Review App now.</p>
              <a href="{{appLink}}">Return to the app</a>
            </body>
            </html>
            """, "text/html");
    }

    // Placeholder Premium endpoint that only proves the paywall works end to end (FeatureKeys.EntitlementPreview).
    // Locked in a later step, remove it when the first real Premium feature exists.
    [HttpGet("entitlement-preview")]
    [Authorize]
    public IActionResult EntitlementPreview() =>
        Ok(new { message = "You have access to Premium features." });

    // Development only: plays a provider event against the current user's Fake subscription
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
