using PingYi.Core;
using PingYi.Infrastructure;
using SkiaSharp;
using ZXing;
using ZXing.QrCode;

namespace PingYi.Core.Tests;

public class QrCodeTests
{
    [Theory]
    [InlineData("https://example.com/path?a=1&b=two#section", false, false)]
    [InlineData("二维码测试：你好，世界！", false, false)]
    [InlineData("https://example.com/rotated", true, false)]
    [InlineData("https://example.com/inverted", false, true)]
    public async Task Decodes_real_qr_pixels_without_models(string text, bool rotated, bool inverted)
    {
        using var bitmap = QrBitmap(text, rotated, inverted);
        var results = await new ZxingQrCodeDecoder().DecodeAsync(Frame(bitmap));
        Assert.Equal(text, Assert.Single(results).Text);
    }

    [Fact]
    public async Task Finds_multiple_codes_and_preserves_payloads()
    {
        using var first = QrBitmap("https://example.com/first");
        using var second = QrBitmap("WIFI:T:WPA;S:Synthetic;P:example-only;;", inverted: true);
        using var composite = new SKBitmap(680, 360);
        using (var canvas = new SKCanvas(composite))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(first, 10, 20); canvas.DrawBitmap(second, 360, 20);
        }
        var results = await new ZxingQrCodeDecoder().DecodeAsync(Frame(composite));
        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Text == "https://example.com/first");
        Assert.Contains(results, r => r.Text.StartsWith("WIFI:") && r.WebUri is null);
    }

    [Fact]
    public async Task Blank_image_returns_no_codes_and_invalid_image_has_fixed_error()
    {
        using var bitmap = new SKBitmap(200, 200);
        bitmap.Erase(SKColors.White);
        var decoder = new ZxingQrCodeDecoder();
        Assert.Empty(await decoder.DecodeAsync(Frame(bitmap)));
        var error = await Assert.ThrowsAsync<ProviderException>(() => decoder.DecodeAsync(
            new ImageFrame([1, 2, 3], 10, 10, new(0, 0, 10, 10))));
        Assert.Equal("qr_image_invalid", error.Code);
    }

    [Fact]
    public async Task Cancelled_decode_does_not_process_image()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ZxingQrCodeDecoder().DecodeAsync(
            new ImageFrame([], 1, 1, new(0, 0, 1, 1)), cts.Token));
    }

    [Theory]
    [InlineData("https://example.com/a?q=1&b=2#fragment")]
    [InlineData("http://127.0.0.1:8080/test")]
    [InlineData("https://例子.测试/说明")]
    [InlineData("  https://example.com/  ")]
    public void Explicit_web_urls_can_open(string text) => Assert.NotNull(new QrCodeResult(text).WebUri);

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("data:text/html,test")]
    [InlineData("ms-settings:")]
    [InlineData("mailto:test@example.com")]
    [InlineData("WIFI:T:WPA;S:test;P:test;;")]
    [InlineData("example.com")]
    [InlineData("https://example.com/\nhttps://other.example/")]
    [InlineData("https://trusted.example@other.example/")]
    [InlineData("https:\\example.com")]
    [InlineData("普通文字")]
    public void Other_payloads_are_copy_only(string text) => Assert.Null(new QrCodeResult(text).WebUri);

    private static SKBitmap QrBitmap(string text, bool rotated = false, bool inverted = false)
    {
        var matrix = new QRCodeWriter().encode(text, BarcodeFormat.QR_CODE, 300, 300,
            new Dictionary<EncodeHintType, object> { [EncodeHintType.CHARACTER_SET] = "UTF-8", [EncodeHintType.MARGIN] = 4 });
        var bitmap = new SKBitmap(matrix.Width, matrix.Height);
        for (var y = 0; y < matrix.Height; y++)
        for (var x = 0; x < matrix.Width; x++)
        {
            var dark = matrix[x, y] != inverted;
            bitmap.SetPixel(rotated ? matrix.Height - 1 - y : x, rotated ? x : y,
                dark ? SKColors.Black : SKColors.White);
        }
        return bitmap;
    }

    private static ImageFrame Frame(SKBitmap bitmap)
    {
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return new(encoded.ToArray(), bitmap.Width, bitmap.Height, new(0, 0, bitmap.Width, bitmap.Height));
    }
}
