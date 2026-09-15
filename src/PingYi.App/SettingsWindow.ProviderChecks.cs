using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using System.Diagnostics;
using PingYi.Core;
using PingYi.Infrastructure;
using SkiaSharp;

namespace PingYi.App;

public partial class SettingsWindow
{
    private async void TestCustomTranslationButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_services is null)
        {
            return;
        }

        var button = sender as Button;
        BeginButtonOperation(button, "正在测试…");
        try
        {
            await TestCustomTranslationConnectionCoreAsync();
            FinishButtonOperation(button, "连接成功", success: true);
        }
        catch (Exception exception)
        {
            SetInlineStatus(CustomTranslationStatusText, UiText.Error(exception), "DangerTextBrush");
            SetGlobalStatus(UiText.Error(exception), isError: true);
            FinishButtonOperation(button, "连接失败", success: false);
        }
    }

    private async void TestCustomVisionButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_services is null)
        {
            return;
        }

        var button = sender as Button;
        BeginButtonOperation(button, "正在测试…");
        try
        {
            await TestCustomVisionConnectionCoreAsync();
            FinishButtonOperation(button, "图片可用", success: true);
        }
        catch (Exception exception)
        {
            SetInlineStatus(CustomTranslationStatusText, UiText.Error(exception), "DangerTextBrush");
            SetGlobalStatus(UiText.Error(exception), isError: true);
            FinishButtonOperation(button, "图片不可用", success: false);
        }
    }

    private async Task ApplyProviderSelectionAsync(bool showStatus)
    {
        if (_services is null)
        {
            return;
        }

        var ocr = OcrProviderCombo.SelectedItem as ProviderChoice;
        var translation = TranslationProviderCombo.SelectedItem as ProviderChoice;
        if (ocr is null || translation is null)
        {
            return;
        }

        await _services.SaveSettingsAsync(_services.Settings with
        {
            OcrProviderId = ocr.Id,
            TranslationProviderId = translation.Id,
            TargetLanguage = (TargetLanguageCombo.SelectedItem as LanguageChoice)?.Code ?? LanguageCatalog.AutoOpposite
        });
        if (showStatus)
        {
            SetGlobalStatus($"已应用：{ocr.Name} + {translation.Name}。", isError: false);
        }
    }

    private static async Task<ProviderAvailability> ProbeOcrProviderAsync(IOcrProvider provider)
    {
        try
        {
            var availability = await provider.GetAvailabilityAsync();
            if (availability.IsAvailable && provider is BaiduOcrProvider baidu)
            {
                await baidu.ValidateCredentialsAsync();
            }
            else if (availability.IsAvailable && provider is GoogleCloudVisionOcrProvider google)
            {
                await google.ValidateCredentialsAsync();
            }

            return availability;
        }
        catch (Exception exception)
        {
            return new ProviderAvailability(false, UiText.Error(exception));
        }
    }

    private static async Task<ProviderAvailability> ProbeTranslationProviderAsync(ITranslationProvider provider)
    {
        try
        {
            var availability = await provider.GetAvailabilityAsync();
            if (availability.IsAvailable && provider is BaiduTranslationProvider baidu)
            {
                await baidu.ValidateCredentialsAsync();
            }
            else if (availability.IsAvailable && provider is GoogleCloudTranslationProvider google)
            {
                await google.ValidateCredentialsAsync();
            }

            return availability;
        }
        catch (Exception exception)
        {
            return new ProviderAvailability(false, UiText.Error(exception));
        }
    }

    private async Task TestCustomTranslationConnectionCoreAsync()
    {
        if (_services is null)
        {
            return;
        }

        SetInlineStatus(CustomTranslationStatusText, "正在连接并发送固定测试文本…", "SecondaryTextBrush");
        await PersistSecretFieldAsync(SecretKeys.CustomTranslationApiKey);
        SelectTranslationProvider("custom-chat");
        await _services.SaveSettingsAsync(BuildSettingsFromForm());
        HideSecretFields(SecretKeys.CustomTranslationApiKey);

        var availability = await _services.CustomTranslationProvider.GetAvailabilityAsync();
        if (!availability.IsAvailable)
        {
            throw new ProviderException("custom_unavailable", availability.Message ?? "大模型服务不可用。");
        }

        var result = await _services.CustomTranslationProvider.TranslateAsync(
            new TranslationRequest("Hello", "en", "zh"));
        if (string.IsNullOrWhiteSpace(result.Text))
        {
            throw new ProviderException("custom_empty", "服务已连接，但没有返回测试译文。");
        }

        SetInlineStatus(CustomTranslationStatusText, "连接、模型名与翻译请求均验证通过", "SuccessTextBrush");
        SetGlobalStatus("本地 / 自定义大模型翻译已可用。", isError: false);
    }

    private async Task TestCustomVisionConnectionCoreAsync()
    {
        if (_services is null)
        {
            return;
        }

        SetInlineStatus(CustomTranslationStatusText, "正在发送固定合成图片测试多模态能力…", "SecondaryTextBrush");
        await PersistSecretFieldAsync(SecretKeys.CustomTranslationApiKey);
        await _services.SaveSettingsAsync(BuildSettingsFromForm());
        HideSecretFields(SecretKeys.CustomTranslationApiKey);

        var availability = await _services.LocalVlmOcrProvider.GetAvailabilityAsync();
        if (!availability.IsAvailable)
        {
            throw new ProviderException("custom_vision_unavailable", availability.Message ?? "多模态模型服务不可用。");
        }

        var result = await _services.LocalVlmOcrProvider.RecognizeAsync(
            CreateVisionTestImage(),
            new OcrOptions("en"));
        if (!result.PlainText.Contains("PINGYI", StringComparison.OrdinalIgnoreCase) ||
            !result.PlainText.Contains("2026", StringComparison.Ordinal))
        {
            throw new ProviderException(
                "custom_vision_mismatch",
                "服务可以接收图片，但未正确读出固定测试文字；请确认模型支持视觉并已加载 mmproj。"
            );
        }

        SetInlineStatus(CustomTranslationStatusText, "连接、模型名与多模态图片识别均验证通过", "SuccessTextBrush");
        SetGlobalStatus("本机 / 自定义大模型图片识别已可用。", isError: false);
    }

    private static ImageFrame CreateVisionTestImage()
    {
        const int width = 360;
        const int height = 96;
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(12, 20, 32));
        using var typeface = SKTypeface.FromFamilyName("Consolas", SKFontStyle.Bold);
        using var font = new SKFont(typeface, 28);
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        canvas.DrawText("PINGYI OCR 2026", 22, 58, SKTextAlign.Left, font, paint);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return new ImageFrame(data.ToArray(), width, height, new PingYi.Core.PixelRect(0, 0, width, height));
    }

}
