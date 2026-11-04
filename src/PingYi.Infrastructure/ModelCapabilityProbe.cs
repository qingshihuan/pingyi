using System.Security.Cryptography;
using PingYi.Core;
using SkiaSharp;

namespace PingYi.Infrastructure;

/// <summary>Capability evidence comes from actual outputs, never from model names or /models.</summary>
public static class ModelCapabilityProbe
{
    public static async Task VerifyTextAsync(ITranslationProvider provider, CancellationToken token)
    {
        var result = await provider.TranslateAsync(new("Hello", "en", "zh"), token);
        if (string.IsNullOrWhiteSpace(result.Text))
            throw new ProviderException("profile_text_empty", "测试未返回文字。 / The test returned no text.");
    }

    public static async Task VerifyVisionAsync(IOcrProvider provider, CancellationToken token)
    {
        // The random answer appears only in the image, never in the request text.
        var challenge = Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
        using var bitmap = new SKBitmap(480, 120);
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true })
        using (var font = new SKFont(SKTypeface.FromFamilyName("monospace"), 40))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawText(challenge, 24, 76, SKTextAlign.Left, font, paint);
        }
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        var result = await provider.RecognizeAsync(new(png.ToArray(), bitmap.Width, bitmap.Height,
            new(0, 0, bitmap.Width, bitmap.Height)), new("en"), token);
        var normalized = string.Concat(result.PlainText.Where(char.IsAsciiLetterOrDigit));
        if (!normalized.Contains(challenge, StringComparison.OrdinalIgnoreCase))
            throw new ProviderException("profile_vision_unverified", "模型未读出测试图片，图片输入能力未验证。 / The model did not read the test image; vision remains unverified.");
    }
}
