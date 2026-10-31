using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReviewApp.Api.Filters;
using ReviewApp.Api.Services;

namespace ReviewApp.Api.Tests.Support;

// A Premium-locked endpoint that exists only in the test host, so the paywall tests don't depend on
// the placeholder entitlement-preview endpoint (which goes away with the first real Premium feature)
[ApiController]
[Route("api/test/premium-probe")]
public class PremiumProbeController : ControllerBase
{
    public const string Route = "api/test/premium-probe";

    [HttpGet]
    [Authorize]
    [RequiresFeature(FeatureKeys.ProfileBadge)]
    public IActionResult Get() => Ok(new { message = "Premium content" });
}
