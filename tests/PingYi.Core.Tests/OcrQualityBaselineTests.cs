using PingYi.Core;
using PingYi.Infrastructure;
using SkiaSharp;
using Xunit.Abstractions;

namespace PingYi.Core.Tests;

public sealed class OcrQualityBaselineTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "LocalModels")]
    public async Task PaddleOcr_MeetsFixedSyntheticSceneThresholds()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("PINGYI_RUN_MODEL_TESTS"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        var cases = new[]
        {
            new BenchmarkCase(
                "clean-bilingual",
                1100,
                280,
                "PingYi OCR 2026 屏幕截图翻译",
                0.95,
                canvas =>
                {
                    DrawLine(canvas, "PingYi OCR 2026", 42, 96, 64, SKColors.Black, true);
                    DrawLine(canvas, "屏幕截图翻译", 42, 216, 64, SKColors.Black, true);
                }),
            new BenchmarkCase(
                "compact-settings",
                1200,
                430,
                "本地模型已就绪 PaddleOCR 与中英翻译均可离线使用 Press Ctrl Alt D to capture",
                0.92,
                canvas =>
                {
                    DrawLine(canvas, "本地模型已就绪", 40, 82, 42, SKColors.Black, true);
                    DrawLine(canvas, "PaddleOCR 与中英翻译均可离线使用", 40, 172, 36, SKColors.Black);
                    DrawLine(canvas, "Press Ctrl Alt D to capture", 40, 262, 36, SKColors.Black);
                }),
            new BenchmarkCase(
                "dark-result-card",
                1180,
                310,
                "识别结果 Translation completed Zero history",
                0.92,
                canvas =>
                {
                    canvas.Clear(new SKColor(10, 29, 48));
                    DrawLine(canvas, "识别结果", 46, 105, 54, SKColors.White, true);
                    DrawLine(canvas, "Translation completed · Zero history", 46, 220, 42, new SKColor(207, 244, 239));
                }),
            new BenchmarkCase(
                "mixed-technical",
                1480,
                320,
                "PingYi v0.1.0 Ctrl Alt D localhost 8080 隐私优先 不保存历史",
                0.90,
                canvas =>
                {
                    DrawLine(canvas, "PingYi v0.1.0 · Ctrl Alt D · localhost 8080", 38, 108, 46, SKColors.Black, true);
                    DrawLine(canvas, "隐私优先，不保存历史", 38, 230, 52, new SKColor(0, 92, 83), true);
                }),
            new BenchmarkCase(
                "small-dark-terminal",
                700,
                130,
                "PS C Users demo C Program Files GitHub CLI gh exe auth login Where do you use GitHub GitHub com What is your preferred protocol for Git operations on this host HTTPS Authenticate Git with your GitHub credentials Yes How would you like to authenticate GitHub CLI Login with a web browser",
                0.90,
                canvas =>
                {
                    canvas.Clear(new SKColor(12, 12, 12));
                    DrawTerminalLine(canvas, 6, 25,
                        ("PS C:\\Users\\demo> & ", new SKColor(220, 220, 220)),
                        ("\"C:\\Program Files\\GitHub CLI\\gh.exe\"", new SKColor(41, 171, 226)),
                        (" auth login", new SKColor(220, 220, 220)));
                    DrawTerminalLine(canvas, 6, 45,
                        ("? ", new SKColor(22, 235, 68)),
                        ("Where do you use GitHub? ", SKColors.White),
                        ("GitHub.com", new SKColor(41, 171, 226)));
                    DrawTerminalLine(canvas, 6, 65,
                        ("? ", new SKColor(22, 235, 68)),
                        ("What is your preferred protocol for Git operations on this host? ", SKColors.White),
                        ("HTTPS", new SKColor(41, 171, 226)));
                    DrawTerminalLine(canvas, 6, 85,
                        ("? ", new SKColor(22, 235, 68)),
                        ("Authenticate Git with your GitHub credentials? ", SKColors.White),
                        ("Yes", new SKColor(41, 171, 226)));
                    DrawTerminalLine(canvas, 6, 105,
                        ("? ", new SKColor(22, 235, 68)),
                        ("How would you like to authenticate GitHub CLI? ", SKColors.White),
                        ("Login with a web browser", new SKColor(41, 171, 226)));
                })
        };

        using var provider = new PaddleOcrProvider(new AppDataPaths());
        var results = new List<BenchmarkResult>();
        foreach (var benchmark in cases)
        {
            using var bitmap = new SKBitmap(benchmark.Width, benchmark.Height);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.White);
            benchmark.Draw(canvas);

            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            var frame = new ImageFrame(
                encoded.ToArray(),
                bitmap.Width,
                bitmap.Height,
                new PixelRect(0, 0, bitmap.Width, bitmap.Height));
            var recognized = await provider.RecognizeAsync(frame, new OcrOptions("auto"));
            results.Add(new BenchmarkResult(
                benchmark.Name,
                benchmark.MinimumSimilarity,
                NormalizedSimilarity(benchmark.Expected, recognized.PlainText),
                recognized.PlainText));
        }

        foreach (var result in results)
        {
            output.WriteLine(
                "{0}: {1:P2} (minimum {2:P0}); actual: {3}",
                result.Name,
                result.ActualSimilarity,
                result.MinimumSimilarity,
                result.ActualText);
        }

        Assert.True(
            results.All(result => result.ActualSimilarity >= result.MinimumSimilarity),
            string.Join(
                Environment.NewLine,
                results.Select(result =>
                    $"{result.Name}: {result.ActualSimilarity:P2} (minimum {result.MinimumSimilarity:P0}); actual: {result.ActualText}")));
    }

    private static void DrawLine(
        SKCanvas canvas,
        string text,
        float x,
        float baseline,
        float size,
        SKColor color,
        bool bold = false)
    {
        using var typeface = SKTypeface.FromFamilyName(
            "Microsoft YaHei",
            bold ? SKFontStyle.Bold : SKFontStyle.Normal);
        using var font = new SKFont(typeface, size);
        using var paint = new SKPaint
        {
            Color = color,
            IsAntialias = true
        };
        canvas.DrawText(text, x, baseline, SKTextAlign.Left, font, paint);
    }

    private static void DrawTerminalLine(
        SKCanvas canvas,
        float x,
        float baseline,
        params (string Text, SKColor Color)[] segments)
    {
        using var typeface = SKTypeface.FromFamilyName("Consolas", SKFontStyle.Bold);
        using var font = new SKFont(typeface, 16);
        using var paint = new SKPaint { IsAntialias = true };
        foreach (var segment in segments)
        {
            paint.Color = segment.Color;
            canvas.DrawText(segment.Text, x, baseline, SKTextAlign.Left, font, paint);
            x += font.MeasureText(segment.Text, paint);
        }
    }

    private static double NormalizedSimilarity(string expected, string actual)
    {
        static string Normalize(string value) => new(
            value.Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());

        var left = Normalize(expected);
        var right = Normalize(actual);
        if (left.Length == 0 && right.Length == 0)
        {
            return 1;
        }

        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];
        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                var substitution = previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1);
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), substitution);
            }

            (previous, current) = (current, previous);
        }

        return 1d - (double)previous[right.Length] / Math.Max(left.Length, right.Length);
    }

    private sealed record BenchmarkCase(
        string Name,
        int Width,
        int Height,
        string Expected,
        double MinimumSimilarity,
        Action<SKCanvas> Draw);

    private sealed record BenchmarkResult(
        string Name,
        double MinimumSimilarity,
        double ActualSimilarity,
        string ActualText);
}
