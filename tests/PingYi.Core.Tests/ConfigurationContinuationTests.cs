using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.Core.Tests;

public sealed class ConfigurationContinuationTests
{
    private static ModelServiceProfile Remote => new()
    {
        Id = "remote-test", Name = "Synthetic remote", Endpoint = "https://example.test/v1",
        DefaultModel = "text-model", Models = [new("text-model", true, false), new("vision-model", false, true)]
    };
    private static AppSettings Mixed => new()
    {
        ManagedRuntimeEnabled = true, ManagedModelPackageId = ManagedMultimodalModels.Recommended.Id,
        CustomTranslationEndpoint = AppSettings.ManagedModelEndpoint,
        ModelConfiguration = new() { Services = [Remote], Tasks = new()
        {
            Ocr = new(ModelConfiguration.Managed), Translation = new(Remote.Id),
            Description = new(Remote.Id, "vision-model", false), Prompt = new(ModelConfiguration.Managed, "", false)
        } }
    };

    [Fact]
    public void Review_reports_per_task_evidence_without_claiming_live_availability()
    {
        var rows = ConfigurationReview.Read(Mixed);
        Assert.Equal(4, rows.Count);
        Assert.Equal(ConfigurationEvidence.ManagedOnDemand, rows[0].Evidence);
        Assert.False(rows[0].Remote);
        Assert.Equal(ConfigurationEvidence.SampleTested, rows[1].Evidence);
        Assert.True(rows[1].Remote); Assert.Equal("https://example.test", rows[1].Destination);
        Assert.Equal("vision-model", rows[2].Model);
        Assert.Equal(ConfigurationEvidence.SampleTested, rows[2].Evidence);
        Assert.False(rows[2].AllowOfflineFallback);
        Assert.Equal(ConfigurationEvidence.ManagedOnDemand, rows[3].Evidence);
    }

    [Fact]
    public void Model_discovery_or_name_does_not_certify_capability()
    {
        var profile = Remote with { Models = [new("vision-model")], DefaultModel = "vision-model" };
        var settings = Mixed with { ModelConfiguration = Mixed.ModelConfiguration! with { Services = [profile] } };
        Assert.Equal(ConfigurationEvidence.Unverified, ConfigurationReview.Read(settings)[2].Evidence);
        profile = profile with { Models = [new("vision-model", true, false)] };
        settings = settings with { ModelConfiguration = settings.ModelConfiguration! with { Services = [profile] } };
        Assert.Equal(ConfigurationEvidence.Unverified, ConfigurationReview.Read(settings)[2].Evidence);
    }

    [Fact]
    public void Invalid_task_destinations_have_sanitized_diagnostics_and_do_not_fall_back_to_another_profile()
    {
        var invalid = Remote with { Endpoint = "https://user:private-key@example.test/v1" };
        var settings = Mixed with { ModelConfiguration = Mixed.ModelConfiguration! with { Services = [invalid] } };
        var row = ConfigurationReview.Read(settings)[1];
        Assert.Equal(ConfigurationEvidence.NeedsConfiguration, row.Evidence);
        Assert.Equal("profile_endpoint_invalid", row.IssueCode);
        Assert.Empty(row.Destination); Assert.DoesNotContain("private-key", row.ToString());
        settings = Mixed with { ModelConfiguration = Mixed.ModelConfiguration! with { Tasks = new() { Translation = new("missing") } } };
        Assert.Equal("task_service_missing", ConfigurationReview.Read(settings)[1].IssueCode);
    }

    [Fact]
    public void Inherited_image_tasks_are_not_mislabelled_as_lightweight_text_tasks()
    {
        var settings = new AppSettings { TranslationProviderId = "local-argos", OcrProviderId = "local-paddle",
            CustomTranslationEndpoint = "https://example.test/v1", CustomTranslationModel = "vision-model" };
        var rows = ConfigurationReview.Read(settings);
        Assert.Equal(ConfigurationEvidence.OfflineComponent, rows[0].Evidence);
        Assert.Equal(ConfigurationEvidence.OfflineComponent, rows[1].Evidence);
        Assert.True(rows[2].Inherited); Assert.True(rows[2].Remote);
        Assert.Equal("vision-model", rows[2].Model);
    }

    [Fact]
    public void Managed_runtime_policy_checks_only_the_requested_task()
    {
        Assert.True(RuntimePolicy.UsesManagedRuntimeForTask(Mixed, ModelTasks.Ocr));
        Assert.False(RuntimePolicy.UsesManagedRuntimeForTask(Mixed, ModelTasks.Translation));
        Assert.False(RuntimePolicy.UsesManagedRuntimeForTask(Mixed, ModelTasks.Description));
        Assert.True(RuntimePolicy.UsesManagedRuntimeForTask(Mixed, ModelTasks.Prompt));
        var onlyImages = Mixed with { ModelConfiguration = Mixed.ModelConfiguration! with
            { Tasks = Mixed.ModelConfiguration.Tasks with { Ocr = new(ModelConfiguration.Offline) } } };
        Assert.False(RuntimePolicy.UsesManagedRuntime(onlyImages));
        Assert.True(RuntimePolicy.UsesAnyManagedRuntime(onlyImages));
        Assert.Throws<ArgumentOutOfRangeException>(() => RuntimePolicy.UsesManagedRuntimeForTask(Mixed, "qr"));
    }

    [Fact]
    public async Task Browser_remote_text_never_starts_runtime_for_the_ocr_route()
    {
        var starts = 0;
        var service = new BrowserTranslationService(new ProviderRegistry([], []), _ => { starts++; return Task.CompletedTask; },
            _ => new Translator(), _ => new Ocr());
        var result = await service.HandleAsync(new BrowserRequest { Operation = "translate", Text = "Hello",
            RouteKey = service.GetStatus(Mixed).RouteKey, AllowRemoteText = true, SourceLanguage = "en", TargetLanguage = "zh" }, Mixed, default);
        Assert.True(result.Ok); Assert.Equal(0, starts);
    }

    [Fact]
    public async Task Browser_same_language_returns_before_loading_a_managed_translator()
    {
        var settings = Mixed with { ModelConfiguration = Mixed.ModelConfiguration! with
            { Tasks = Mixed.ModelConfiguration.Tasks with { Translation = new(ModelConfiguration.Managed) } } };
        var starts = 0;
        var service = new BrowserTranslationService(new ProviderRegistry([], []), _ => { starts++; return Task.CompletedTask; },
            _ => new Translator(), _ => new Ocr());
        var result = await service.HandleAsync(new BrowserRequest { Operation = "translate", Text = "Hello",
            RouteKey = service.GetStatus(settings).RouteKey, AllowRemoteText = true, SourceLanguage = "en", TargetLanguage = "en" }, settings, default);
        Assert.True(result.Ok); Assert.Equal(0, starts); Assert.Equal("Hello", result.Translation);
    }

    private sealed class Translator : ITranslationProvider
    {
        public ProviderMetadata Metadata { get; } = new("custom-chat", "Synthetic remote", ProviderExecutionLocation.Cloud, false, false, ["en", "zh"]);
        public ValueTask<ProviderAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(ProviderAvailability.Available);
        public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TranslationResult("你好", request.SourceLanguage, request.TargetLanguage));
    }
    private sealed class Ocr : IOcrProvider
    {
        public ProviderMetadata Metadata { get; } = new("local-vlm-ocr", "Synthetic local", ProviderExecutionLocation.Local, true, false, ["en"]);
        public ValueTask<ProviderAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(ProviderAvailability.Available);
        public Task<OcrResult> RecognizeAsync(ImageFrame image, OcrOptions options, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Text requests must not execute OCR.");
    }
}
