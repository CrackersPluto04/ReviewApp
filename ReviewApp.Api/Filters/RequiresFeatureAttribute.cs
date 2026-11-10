using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Filters;

/// <summary>
/// Locks an endpoint behind a feature (see <see cref="Services.Entitlements"/>). Use it together with
/// <c>[Authorize]</c>. A user without access gets 402 <c>{ error, code: "premium_required", feature }</c>, which the
/// clients turn into the upsell. The tier is read from the database on every request, so subscribing or expiring
/// takes effect at once.
/// </summary>
public class RequiresFeatureAttribute : TypeFilterAttribute
{
    public const string PremiumRequiredCode = "premium_required";

    /// <param name="featureKey">A key from <see cref="Services.FeatureKeys"/>.</param>
    public RequiresFeatureAttribute(string featureKey) : base(typeof(RequiresFeatureFilter))
    {
        Arguments = [featureKey];
    }

    private sealed class RequiresFeatureFilter : IAsyncActionFilter
    {
        private readonly string _featureKey;
        private readonly IEntitlementService _entitlementService;
        private readonly IUserAuthHelper _userAuthHelper;

        public RequiresFeatureFilter(string featureKey, IEntitlementService entitlementService, IUserAuthHelper userAuthHelper)
        {
            _featureKey = featureKey;
            _entitlementService = entitlementService;
            _userAuthHelper = userAuthHelper;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var userId = _userAuthHelper.GetOptionalUserID();
            if (userId == null)
            {
                context.Result = new UnauthorizedObjectResult(new { error = "You need to log in." });
                return;
            }

            if (!await _entitlementService.HasFeatureAsync(userId.Value, _featureKey))
            {
                context.Result = new ObjectResult(new
                {
                    error = "This is a Premium feature.",
                    code = PremiumRequiredCode,
                    feature = _featureKey
                })
                { StatusCode = StatusCodes.Status402PaymentRequired };
                return;
            }

            await next();
        }
    }
}
