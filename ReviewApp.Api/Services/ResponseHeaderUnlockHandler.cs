using ReviewApp.Api.DTOs;
using ReviewApp.Api.Services.Interfaces;
using System.Text.Json;

namespace ReviewApp.Api.Services;

/// <summary>
/// Puts the tiers unlocked during a request into the <c>X-Unlocked-Achievements</c> response header. The clients
/// read it and show a toast, so no endpoint's response body has to change. Only the user who made the request is
/// told, and several evaluations within one request are collected into a single header.
/// </summary>
public class ResponseHeaderUnlockHandler : IAchievementUnlockHandler
{
    /// <summary>The header's name; it is also exposed in the CORS policy so the browser lets the frontend read it.</summary>
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
