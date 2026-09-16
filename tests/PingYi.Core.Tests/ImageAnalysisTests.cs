using System.Net;
using System.Text;
using System.Text.Json;
using PingYi.Core;
using PingYi.Infrastructure;
using SkiaSharp;

namespace PingYi.Core.Tests;

public class ImageAnalysisTests
{
    [Theory]
    [InlineData(CapturePurpose.DescribeImage, "zh-CN")]
    [InlineData(CapturePurpose.ReconstructPrompt, "en-US")]
    public async Task Pure_image_is_sent_directly_as_image_url_with_the_requested_task(CapturePurpose purpose, string language)
    {
        var requests = 0;
        using var client = new HttpClient(new Handler(async (request, token) =>
        {
            requests++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("test-key", request.Headers.Authorization?.Parameter);
            using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var root = doc.RootElement;
            Assert.Equal("vision-test", root.GetProperty("model").GetString());
            var messages = root.GetProperty("messages");
            Assert.Contains("untrusted", messages[0].GetProperty("content").GetString());
            var content = messages[1].GetProperty("content");
            Assert.Equal(ImageAnalysisPrompts.Build(new(purpose, language)), content[0].GetProperty("text").GetString());
            var dataUrl = content[1].GetProperty("image_url").GetProperty("url").GetString()!;
            Assert.StartsWith("data:image/png;base64,", dataUrl);
            using var decoded = SKBitmap.Decode(Convert.FromBase64String(dataUrl.Split(',')[1]));
            Assert.Equal(1600, decoded.Width);
            Assert.Equal(800, decoded.Height);
            Assert.False(root.GetProperty("stream").GetBoolean());
            return Json("{\"choices\":[{\"message\":{\"content\":\"A blue circle on a plain background.\"}}]}");
        }));
        var result = await Provider(client).AnalyzeAsync(Image(2000, 1000), new(purpose, language));
        Assert.Equal(1, requests); // No OCR, translation or automatic fallback call.
        Assert.Equal(purpose, result.Purpose);
        Assert.Equal("A blue circle on a plain background.", result.Text);
    }

    [Theory]
    [InlineData("[]", "image_analysis_schema")]
    [InlineData("{\"choices\":[]}", "image_analysis_schema")]
    [InlineData("{\"choices\":[{\"message\":{\"content\":\" \"}}]}", "image_analysis_empty")]
    [InlineData("{\"choices\":[{\"message\":{\"content\":123}}]}", "image_analysis_schema")]
    public async Task Empty_and_malformed_responses_fail_without_fake_success(string json, string code)
    {
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(Json(json))));
        var error = await Assert.ThrowsAsync<ProviderException>(() => Provider(client).AnalyzeAsync(Image(), new(CapturePurpose.DescribeImage)));
        Assert.Equal(code, error.Code);
    }

    [Fact]
    public async Task Text_parts_and_length_limited_responses_are_handled_explicitly()
    {
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(Json(
            "{\"choices\":[{\"finish_reason\":\"length\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"First\"},{\"type\":\"text\",\"text\":\"Second\"}]}}]}"))));
        var result = await Provider(client).AnalyzeAsync(Image(), new(CapturePurpose.ReconstructPrompt, "en-US"));
        Assert.StartsWith("First\nSecond", result.Text);
        Assert.Contains("may be incomplete", result.Text);
    }

    [Fact]
    public async Task Http_errors_do_not_echo_remote_sensitive_bodies_or_fall_back_to_ocr()
    {
        var requests = 0;
        using var client = new HttpClient(new Handler((_, _) =>
        {
            requests++;
            return Task.FromResult(Json("private image text and test-key", HttpStatusCode.BadRequest));
        }));
        var error = await Assert.ThrowsAsync<ProviderException>(() => Provider(client).AnalyzeAsync(Image(), new(CapturePurpose.DescribeImage)));
        Assert.Equal("image_analysis_http", error.Code);
        Assert.DoesNotContain("private image text", error.Message);
        Assert.DoesNotContain("test-key", error.ToString());
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task Oversized_response_is_rejected_without_reading_unbounded_content()
    {
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(Json(new string('x', ChatCompatibleImageAnalysisProvider.MaximumResponseBytes + 1)))));
        var error = await Assert.ThrowsAsync<ProviderException>(() => Provider(client).AnalyzeAsync(Image(), new(CapturePurpose.DescribeImage)));
        Assert.Equal("image_analysis_response_large", error.Code);
    }

    [Theory]
    [InlineData("http://remote.example/v1/chat/completions", "custom_endpoint_insecure_transport")]
    [InlineData("not a URI", "image_analysis_configuration")]
    public async Task Invalid_or_insecure_destinations_never_send_the_image(string endpoint, string code)
    {
        using var client = new HttpClient(new Handler((_, _) => throw new InvalidOperationException("No network expected")));
        var error = await Assert.ThrowsAsync<ProviderException>(() => Provider(client, endpoint).AnalyzeAsync(Image(), new(CapturePurpose.DescribeImage)));
        Assert.Equal(code, error.Code);
    }

    [Fact]
    public async Task Corrupted_png_is_rejected_before_the_request()
    {
        using var client = new HttpClient(new Handler((_, _) => throw new InvalidOperationException("No network expected")));
        var error = await Assert.ThrowsAsync<ProviderException>(() => Provider(client).AnalyzeAsync(new([1,2,3], 10, 10, default), new(CapturePurpose.DescribeImage)));
        Assert.Equal("image_analysis_image_invalid", error.Code);
    }

    [Fact]
    public async Task Caller_cancellation_is_preserved()
    {
        using var cts = new CancellationTokenSource();
        using var client = new HttpClient(new Handler(async (_, token) =>
        {
            cts.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException();
        }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Provider(client).AnalyzeAsync(Image(), new(CapturePurpose.DescribeImage), cts.Token));
    }

    [Fact]
    public void Translation_task_cannot_be_misrouted_to_vision_and_managed_analysis_is_independent_of_ocr_choice()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageAnalysisPrompts.Build(new(CapturePurpose.TranslateText)));
        var settings = new AppSettings { ManagedRuntimeEnabled = true, ManagedModelPackageId = ManagedMultimodalModels.Recommended.Id,
            CustomTranslationEndpoint = AppSettings.ManagedModelEndpoint };
        Assert.False(RuntimePolicy.UsesManagedRuntime(settings));
        Assert.True(RuntimePolicy.HasConfiguredManagedRuntime(settings));
        Assert.Contains("original prompt", ImageAnalysisPrompts.System);
    }

    private static ChatCompatibleImageAnalysisProvider Provider(HttpClient client, string endpoint = "http://127.0.0.1:8080/v1/chat/completions") =>
        new(client, new Secrets(), new AppSettings { CustomTranslationEndpoint = endpoint, CustomTranslationModel = "vision-test" });
    private static ImageFrame Image(int width = 96, int height = 64)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        using var paint = new SKPaint { Color = SKColors.Blue, IsAntialias = true };
        canvas.DrawCircle(width / 2f, height / 2f, height / 3f, paint);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return new(data.ToArray(), width, height, new(0, 0, width, height));
    }
    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
    private sealed class Secrets : ISecretStore
    {
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult<string?>("test-key");
        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
