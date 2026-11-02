using System.Buffers.Binary;

namespace ReviewApp.Api.Services.Recognition;

public enum ImageFormat
{
    Jpeg,
    Png
}

public readonly record struct ImageInfo(ImageFormat Format, int Width, int Height)
{
    public string MimeType => Format == ImageFormat.Png ? "image/png" : "image/jpeg";
    public long Pixels => (long)Width * Height;
}

// Recognises an uploaded image by its content (never by the file name or the client's content type) and reads its
// dimensions from the header. Nothing is decoded, so a "decompression bomb" costs nothing: its header gives it away.
// The clients always send JPEG; PNG is accepted for picked screenshots. Anything else (HEIC, WebP, GIF, not an image) is refused.
public static class ImageInspector
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static ImageInfo? Inspect(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return InspectJpeg(data);

        if (data.StartsWith(PngSignature))
            return InspectPng(data);

        return null;
    }

    // Walks the marker segments up to the first frame header (SOF0..SOF15, except DHT, JPG and DAC)
    private static ImageInfo? InspectJpeg(ReadOnlySpan<byte> data)
    {
        var position = 2;
        while (position + 1 < data.Length)
        {
            if (data[position] != 0xFF)
                return null;

            var marker = data[position + 1];
            // Fill bytes before a marker
            if (marker == 0xFF)
            {
                position++;
                continue;
            }

            // Markers without a length
            if (marker == 0x01 || marker is >= 0xD0 and <= 0xD7)
            {
                position += 2;
                continue;
            }

            // End of image or start of scan before any frame header: no dimensions to read
            if (marker is 0xD9 or 0xDA)
                return null;

            if (position + 4 > data.Length)
                return null;

            var length = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(position + 2, 2));
            if (length < 2)
                return null;

            var isFrameHeader = marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC);
            if (isFrameHeader)
            {
                // Length (2), precision (1), height (2), width (2)
                if (length < 7 || position + 9 > data.Length)
                    return null;

                var height = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(position + 5, 2));
                var width = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(position + 7, 2));
                return width > 0 && height > 0 ? new ImageInfo(ImageFormat.Jpeg, width, height) : null;
            }

            position += 2 + length;
        }

        return null;
    }

    // The IHDR chunk always comes first: length (4), "IHDR" (4), width (4), height (4)
    private static ImageInfo? InspectPng(ReadOnlySpan<byte> data)
    {
        if (data.Length < 24 || !data.Slice(12, 4).SequenceEqual("IHDR"u8))
            return null;

        var width = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(16, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(20, 4));
        if (width == 0 || height == 0 || width > int.MaxValue || height > int.MaxValue)
            return null;

        return new ImageInfo(ImageFormat.Png, (int)width, (int)height);
    }
}
