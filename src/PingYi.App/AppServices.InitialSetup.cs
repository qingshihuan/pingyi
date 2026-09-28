using PingYi.Core;
using PingYi.Infrastructure;
using SkiaSharp;

namespace PingYi.App;

public sealed partial class AppServices
{
    public bool IsInitialSetupActive { get; internal set; }

    /// <summary>Explicit user action only. Failed/cancelled downloads never mark setup complete.</summary>
    public async Task ConfigureInitialModelAsync(ManagedMultimodalModel model, string backend,
        IProgress<ManagedModelProgress> progress, CancellationToken token)
    {
        var before = Settings;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        var cancellation = lifetime.Token;
        if (!ManagedModels.HasBundledRuntime)
            throw new ProviderException("managed_runtime_unavailable", "此安装目录缺少 llama.cpp 运行时，请安装完整软件包或使用已有本机服务／轻量模式。");
        await ManagedModels.DownloadAsync(model, progress, cancellation);
        await ManagedModels.EnsureStartedAsync(model, backend, progress, cancellation);
        var configured = before with
        {
            OcrProviderId = "local-vlm-ocr", TranslationProviderId = "custom-chat",
            CustomTranslationEndpoint = AppSettings.ManagedModelEndpoint, CustomTranslationModel = model.ModelAlias,
            ManagedModelPackageId = model.Id, ManagedRuntimeBackend = backend, ManagedRuntimeEnabled = true,
            InitialSetupCompleted = true
        };
        // Verify both request types using synthetic input, not the user's screenshot or clipboard.
        using var verification = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        verification.CancelAfter(TimeSpan.FromMinutes(4));
        var translator = new ChatCompatibleTranslationProvider(_imageAnalysisClient, SecretStore, () => configured);
        var ocr = new ChatCompatibleOcrProvider(_imageAnalysisClient, SecretStore, () => configured, translator);
        using var bitmap = new SKBitmap(360, 90);
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true })
        using (var font = new SKFont(SKTypeface.Default, 32))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawText("Hello PingYi", 15, 58, font, paint);
        }
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        await ocr.RecognizeAsync(new ImageFrame(png.ToArray(), 360, 90, new PixelRect(0, 0, 360, 90)),
            new OcrOptions("en"), verification.Token);
        await translator.TranslateAsync(new TranslationRequest("Hello", "en", "zh"), verification.Token);
        cancellation.ThrowIfCancellationRequested();
        if (Settings != before)
            throw new ProviderException("initial_setup_changed", "配置在下载期间发生变化，请重新应用模型；现有配置未被覆盖。");
        await SaveSettingsAsync(configured, cancellation);
    }
}
