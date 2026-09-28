using PingYi.Core;
using SkiaSharp;
using ZXing;
using ZXing.Common;

namespace PingYi.Infrastructure;

/// <summary>Offline decoding only: no OCR engine, provider settings, network or retained images.</summary>
public sealed class ZxingQrCodeDecoder : IQrCodeDecoder
{
    private const long MaximumPixels = 16_777_216;

    public Task<IReadOnlyList<QrCodeResult>> DecodeAsync(ImageFrame image,
        CancellationToken cancellationToken = default) => Task.Run(() => Decode(image, cancellationToken), cancellationToken);

    private static IReadOnlyList<QrCodeResult> Decode(ImageFrame image, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (image.PngBytes.Length is 0 or > 33_554_432)
            throw new ProviderException("qr_image_invalid", "二维码截图无效或过大，请缩小框选范围。");
        using var stream = new SKMemoryStream(image.PngBytes);
        using var codec = SKCodec.Create(stream);
        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0 ||
            (long)codec.Info.Width * codec.Info.Height > MaximumPixels)
            throw new ProviderException("qr_image_invalid", "二维码截图无效或过大，请缩小框选范围。");
        using var decoded = SKBitmap.Decode(codec);
        if (decoded is null) throw new ProviderException("qr_image_invalid", "二维码截图无法读取。");
        token.ThrowIfCancellationRequested();
        using var bitmap = new SKBitmap(decoded.Width, decoded.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(decoded, 0, 0);
        }
        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                TryHarder = true,
                PossibleFormats = [BarcodeFormat.QR_CODE]
            }
        };
        var source = new RGBLuminanceSource(bitmap.Bytes, bitmap.Width, bitmap.Height,
            RGBLuminanceSource.BitmapFormat.RGBA32);
        var results = reader.DecodeMultiple(source) ?? [];
        token.ThrowIfCancellationRequested();
        // The multiple-QR reader does not apply TryInverted. Scan both polarities,
        // also covering captures that contain dark and light QR codes together.
        var inverted = reader.DecodeMultiple(source.invert()) ?? [];
        token.ThrowIfCancellationRequested();
        return results.Concat(inverted).Where(result => !string.IsNullOrEmpty(result.Text))
            .DistinctBy(result => result.Text, StringComparer.Ordinal)
            .Select(result => new QrCodeResult(result.Text)).ToArray();
    }
}
