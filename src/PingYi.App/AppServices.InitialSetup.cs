using PingYi.Core;
using PingYi.Infrastructure;
using SkiaSharp;

namespace PingYi.App;

public sealed partial class AppServices
{
    private readonly SemaphoreSlim _initialModelGate = new(1, 1);
    public bool IsInitialSetupActive { get; internal set; }

    public async Task SaveAutomaticCapturePreferenceAsync(bool enabled, CancellationToken token = default)
    {
        await _settingsTransitionGate.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
            var next = Settings with { AutomaticCaptureEnabled = enabled };
            await SettingsStore.SaveAsync(next, token);
            Settings = next;
            // A task-selection preference must not start a large model.
        }
        finally { _settingsTransitionGate.Release(); }
    }

    /// <summary>Explicit user action only; success means both request types were verified.</summary>
    public Task ConfigureInitialModelAsync(ManagedMultimodalModel model, string backend,
        IProgress<ManagedModelProgress> progress, CancellationToken token) =>
        ConfigureInitialModelAsync(model, backend, Settings.ManagedRuntimeDevice, Settings.RuntimeAllowMirrors,
            Settings.RuntimeMirrorPrefixes, progress, token);

    public async Task ConfigureInitialModelAsync(ManagedMultimodalModel model, string backend, string device,
        bool allowMirrors, string mirrorPrefixes, IProgress<ManagedModelProgress> progress, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(IsShuttingDown, this);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        var cancellation = lifetime.Token;
        await _initialModelGate.WaitAsync(cancellation);
        var previouslyActive = IsInitialSetupActive;
        IsInitialSetupActive = true;
        var before = Settings;
        var attemptedStart = false;
        try
        {
            await ManagedModels.Runtimes.InstallAsync(backend, device, allowMirrors,
                mirrorPrefixes.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries), progress, cancellation);
            await ManagedModels.DownloadAsync(model, progress, cancellation);
            attemptedStart = true;
            await ManagedModels.EnsureStartedAsync(model, backend, progress, cancellation, device);
            var configured = before with
            {
                OcrProviderId = "local-vlm-ocr", TranslationProviderId = "custom-chat",
                CustomTranslationEndpoint = AppSettings.ManagedModelEndpoint, CustomTranslationModel = model.ModelAlias,
                ManagedModelPackageId = model.Id, ManagedRuntimeBackend = backend, ManagedRuntimeEnabled = true,
                ManagedRuntimeDevice = device, RuntimeAllowMirrors = allowMirrors, RuntimeMirrorPrefixes = mirrorPrefixes,
                InitialSetupCompleted = true
            };
            using var verification = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            verification.CancelAfter(TimeSpan.FromMinutes(4));
            var translator = new ChatCompatibleTranslationProvider(_imageAnalysisClient, SecretStore, () => configured);
            var ocr = new ChatCompatibleOcrProvider(_imageAnalysisClient, SecretStore, () => configured, translator);
            using var bitmap = new SKBitmap(360, 96);
            using (var canvas = new SKCanvas(bitmap))
            using (var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true })
            using (var font = new SKFont(SKTypeface.Default, 28))
            {
                canvas.Clear(SKColors.White);
                canvas.DrawText("PINGYI OCR 2026", 18, 58, SKTextAlign.Left, font, paint);
            }
            using var synthetic = SKImage.FromBitmap(bitmap);
            using var png = synthetic.Encode(SKEncodedImageFormat.Png, 100);
            var recognized = await ocr.RecognizeAsync(new ImageFrame(png.ToArray(), 360, 96, new PixelRect(0, 0, 360, 96)),
                new OcrOptions("en"), verification.Token);
            if (!recognized.PlainText.Contains("PINGYI", StringComparison.OrdinalIgnoreCase) ||
                !recognized.PlainText.Contains("2026", StringComparison.Ordinal))
                throw new ProviderException("custom_vision_mismatch", "模型未正确读出固定测试图片，请检查视觉组件或选择其他模型。");
            var translated = await translator.TranslateAsync(new TranslationRequest("Hello", "en", "zh"), verification.Token);
            if (string.IsNullOrWhiteSpace(translated.Text))
                throw new ProviderException("custom_empty", "模型未返回测试译文，未应用本次配置。");
            cancellation.ThrowIfCancellationRequested();
            // Serialize the compare-and-save with language, browser and settings transitions.
            await _settingsTransitionGate.WaitAsync(cancellation);
            try
            {
                if (Settings != before)
                    throw new ProviderException("initial_setup_changed", "配置在下载期间发生变化，请重新应用模型；现有配置未被覆盖。");
                await SettingsStore.SaveAsync(configured.Normalize(), cancellation);
                Settings = configured.Normalize();
            }
            finally { _settingsTransitionGate.Release(); }
        }
        catch
        {
            // An uncommitted model must not remain running after Skip/Cancel. A previous
            // managed configuration is retained and can start again on its next request.
            if (attemptedStart && Settings == before && !IsShuttingDown)
            {
                try { await ManagedModels.StopAsync(_lifetime.Token); }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            }
            throw;
        }
        finally { IsInitialSetupActive = previouslyActive; _initialModelGate.Release(); }
    }
}
