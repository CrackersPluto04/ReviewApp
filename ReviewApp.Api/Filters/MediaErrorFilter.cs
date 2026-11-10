using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ReviewApp.Api.Services;

namespace ReviewApp.Api.Filters;

/// <summary>
/// Global exception filter: a media item that could not be resolved from TMDb / Spotify (while creating a review or
/// adding to a collection) answers 404 <c>media_not_found</c>, or 503 <c>source_unavailable</c> when the source is
/// down. Nothing was stored. The two are kept apart so a client never reports an outage as "this title does not
/// exist". Handling it here keeps the services free of HTTP concerns.
/// </summary>
public class MediaErrorFilter : IExceptionFilter
{
    public const string NotFoundCode = "media_not_found";
    public const string UnavailableCode = "source_unavailable";

    /// <summary>Turns a <see cref="MediaResolutionException"/> into its response; any other exception is left alone.</summary>
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not MediaResolutionException ex)
            return;

        context.Result = ex.Status switch
        {
            MediaLookupStatus.Unavailable => new ObjectResult(new { error = UnavailableMessage, code = UnavailableCode })
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable
            },
            MediaLookupStatus.InvalidType => new BadRequestObjectResult(new { error = "Invalid media type." }),
            _ => new NotFoundObjectResult(new { error = NotFoundMessage, code = NotFoundCode })
        };
        context.ExceptionHandled = true;
    }

    public const string NotFoundMessage = "This title could not be found.";
    public const string UnavailableMessage = "The media service is temporarily unavailable. Please try again later.";
}
