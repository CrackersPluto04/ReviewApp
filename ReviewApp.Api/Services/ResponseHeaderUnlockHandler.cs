using ReviewApp.Api.DTOs;
using ReviewApp.Api.Services.Interfaces;
using System.Text.Json;

namespace ReviewApp.Api.Services;

// Puts the tiers unlocked during a request into the X-Unlocked-Achievements response header,
// the frontend's apiFetch reads it and shows a toast. This way no endpoint's response body has to change.
public class ResponseHeaderUnlockHandler : IAchievementUnlockHandler
{
    public const string HeaderName = "X-Unlocked-Achievements";
    private const string ItemsKey = "UnlockedAchievements";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IUserAuthHelper _userAuthHelper;

    public ResponseHeaderUnlockHandler(IHttpContextAccessor httpContextAccessor, IUserAuthHelper userAuthHelper)
    {
        _httpContextAccessor = httpContextAccessor;
        _userAuthHelper = userAuthHelper;
    }

    public Task HandleAsync(int userId, IReadOnlyList<UnlockedAchievementDto> unlocked)
    {
        var httpContext = _httpContextAccessor.HttpContext;

        // Only tell the user who made the request, not others whose progress changed along the way
        if (httpContext == null || _userAuthHelper.GetOptionalUserID() != userId || httpContext.Response.HasStarted)
            return Task.CompletedTask;

        // Several evaluations can run in one request, collect them and write the header once
        if (httpContext.Items[ItemsKey] is not List<UnlockedAchievementDto> pending)
        {
            pending = [];
            httpContext.Items[ItemsKey] = pending;

            httpContext.Response.OnStarting(() =>
            {
                // Default encoder escapes non-ASCII characters, so the value is a valid header
                httpContext.Response.Headers[HeaderName] = JsonSerializer.Serialize(pending, JsonOptions);
                return Task.CompletedTask;
            });
        }

        pending.AddRange(unlocked);
        return Task.CompletedTask;
    }
}
