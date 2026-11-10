using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace ReviewApp.Api.Services.Recognition;

/// <summary>How reading the uploaded photo ended.</summary>
public enum ImageReadStatus
{
    Ok,
    /// <summary>Not a <c>multipart/form-data</c> request, or a malformed one.</summary>
    NotMultipart,
    /// <summary>No <c>image</c> file part.</summary>
    Missing,
    TooLarge
}

/// <summary>The result of reading the upload; <see cref="Data"/> is set only for <see cref="ImageReadStatus.Ok"/>.</summary>
public readonly record struct ImageReadResult(ImageReadStatus Status, byte[]? Data = null);

/// <summary>
/// Reads the uploaded photo (the <c>image</c> part of a <c>multipart/form-data</c> request) straight into memory,
/// with a hard size cap. <c>IFormFile</c> and <c>Request.Form</c> are not used on purpose: they buffer larger files
/// to a temp file on disk, and the photo must never touch the disk.
/// </summary>
public static class MultipartImageReader
{
    /// <summary>The name of the form field that carries the photo.</summary>
    public const string PartName = "image";

    // Room for the boundaries and part headers around the photo
    private const int MultipartOverhead = 16 * 1024;
    private const int MaxBoundaryLength = 70; // RFC 2046

    /// <summary>
    /// Reads the photo from the request body. The cap is enforced twice: a declared <c>Content-Length</c> over the
    /// cap is refused up front, and the part itself is counted while it is read, because the length can be absent or false.
    /// </summary>
    /// <param name="request">The upload request.</param>
    /// <param name="maxBytes">The most bytes the photo may have.</param>
    /// <param name="cancellationToken">Cancelled when the client disconnects.</param>
    public static async Task<ImageReadResult> ReadAsync(HttpRequest request, int maxBytes, CancellationToken cancellationToken)
    {
        if (request.ContentLength > maxBytes + MultipartOverhead)
            return new(ImageReadStatus.TooLarge);

        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType)
            || !contentType.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase))
            return new(ImageReadStatus.NotMultipart);

        var boundary = HeaderUtilities.RemoveQuotes(contentType.Boundary).Value;
        if (string.IsNullOrWhiteSpace(boundary) || boundary.Length > MaxBoundaryLength)
            return new(ImageReadStatus.NotMultipart);

        var reader = new MultipartReader(boundary, request.Body) { HeadersCountLimit = 8, HeadersLengthLimit = 4 * 1024 };
        try
        {
            while (await reader.ReadNextSectionAsync(cancellationToken) is { } section)
            {
                if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)
                    || !disposition.IsFileDisposition()
                    || !string.Equals(HeaderUtilities.RemoveQuotes(disposition.Name).Value, PartName, StringComparison.Ordinal))
                    continue; // other parts are skipped (the reader drains them)

                var data = await ReadCappedAsync(section.Body, maxBytes, cancellationToken);
                return data == null ? new(ImageReadStatus.TooLarge) : new(ImageReadStatus.Ok, data);
            }

            return new(ImageReadStatus.Missing);
        }
        catch (InvalidDataException)
        {
            // Malformed multipart body (bad boundary, oversized headers)
            return new(ImageReadStatus.NotMultipart);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return new(ImageReadStatus.TooLarge);
        }
        catch (IOException)
        {
            // Body ended early (client gave up mid-upload)
            return new(ImageReadStatus.NotMultipart);
        }
    }

    // Null when the part is larger than maxBytes
    private static async Task<byte[]?> ReadCappedAsync(Stream body, int maxBytes, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (memory.Length + read > maxBytes)
                return null;
            memory.Write(buffer, 0, read);
        }
        return memory.ToArray();
    }
}
