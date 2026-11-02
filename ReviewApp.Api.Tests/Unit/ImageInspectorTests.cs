using ReviewApp.Api.Services.Recognition;
using ReviewApp.Api.Tests.Support;

namespace ReviewApp.Api.Tests.Unit;

// Uploaded photos are recognised by their bytes, and their size is read from the header without decoding
public class ImageInspectorTests
{
    [Fact]
    public void Jpeg_ReadsDimensions_PastOtherSegments()
    {
        var info = ImageInspector.Inspect(TestImages.Jpeg("x", width: 1280, height: 960));

        info.ShouldBe(new ImageInfo(ImageFormat.Jpeg, 1280, 960));
        info!.Value.MimeType.ShouldBe("image/jpeg");
    }

    [Fact]
    public void ProgressiveJpeg_IsReadToo()
    {
        var image = TestImages.Jpeg(width: 300, height: 200);
        image[Array.IndexOf(image, (byte)0xC0)] = 0xC2;

        ImageInspector.Inspect(image).ShouldBe(new ImageInfo(ImageFormat.Jpeg, 300, 200));
    }

    [Fact]
    public void Jpeg_HuffmanTableBeforeFrame_IsNotMistakenForAFrame()
    {
        // FF C4 (DHT) has a frame-like marker number but is a table; the real frame follows
        byte[] image = [0xFF, 0xD8, 0xFF, 0xC4, 0x00, 0x07, 0x00, 0x09, 0x09, 0x09, 0x09,
            0xFF, 0xC0, 0x00, 0x11, 0x08, 0x00, 0x10, 0x00, 0x20, 0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01, 0xFF, 0xD9];

        ImageInspector.Inspect(image).ShouldBe(new ImageInfo(ImageFormat.Jpeg, 32, 16));
    }

    [Fact]
    public void Png_ReadsDimensions()
    {
        var info = ImageInspector.Inspect(TestImages.Png(640, 480));

        info.ShouldBe(new ImageInfo(ImageFormat.Png, 640, 480));
        info!.Value.MimeType.ShouldBe("image/png");
    }

    [Fact]
    public void HugePixelCount_IsVisibleFromTheHeaderAlone()
    {
        // A "decompression bomb": a few bytes that claim 65535 x 65535 pixels
        var info = ImageInspector.Inspect(TestImages.Jpeg(width: 65535, height: 65535));

        info!.Value.Pixels.ShouldBe(65535L * 65535L);
    }

    [Fact]
    public void OtherFormats_AreRefused()
    {
        ImageInspector.Inspect(TestImages.Heic()).ShouldBeNull();
        ImageInspector.Inspect("GIF89a\u0001\u0000\u0001\u0000"u8).ShouldBeNull();
        ImageInspector.Inspect("RIFF\u0000\u0000\u0000\u0000WEBPVP8 "u8).ShouldBeNull();
        ImageInspector.Inspect("%PDF-1.7"u8).ShouldBeNull();
        ImageInspector.Inspect([]).ShouldBeNull();
    }

    [Fact]
    public void TruncatedOrCorruptHeaders_AreRefused()
    {
        var jpeg = TestImages.Jpeg("x");
        ImageInspector.Inspect(jpeg.AsSpan(0, 12)).ShouldBeNull();
        ImageInspector.Inspect([0xFF, 0xD8, 0xFF, 0xDA, 0x00, 0x02]).ShouldBeNull(); // scan before any frame
        ImageInspector.Inspect([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x01]).ShouldBeNull(); // impossible segment length
        ImageInspector.Inspect(TestImages.Png().AsSpan(0, 20)).ShouldBeNull();
        ImageInspector.Inspect(TestImages.Jpeg(width: 0, height: 10)).ShouldBeNull();
    }
}
