using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace ReviewApp.Api.Services.Recognition;

public enum ImageReadStatus
{
    Ok,
    NotMultipart, // not a multipart/form-data request, or a malformed one
    Missing,      // no "image" file part
    TooLarge
}

public readonly record struct ImageReadResult(ImageReadStatus Status, byte[]? Data = null);

// Reads the uploaded photo (the "image" part of a multipart/form-data request) straight into memory, with a hard size cap.
// IFormFile / Request.Form are not used on purpose: they buffer larger files to a temp file on disk, and the photo must never touch the disk.
public static class MultipartImageReader
{
    public const string PartName = "image";

    // Room for the boundaries and part headers around the photo
    private const int MultipartOverhead = 16 * 1024;
    private const int MaxBoundaryLength = 70; // RFC 2046

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
