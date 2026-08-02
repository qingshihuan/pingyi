using PingYi.Core;

namespace PingYi.Core.Tests;

public sealed class ManagedMultimodalModelsTests
{
    [Fact]
    public void Catalog_UsesPinnedModelScopeFilesWithCompleteIntegrityMetadata()
    {
        Assert.Equal(
            ManagedMultimodalModels.All.Count,
            ManagedMultimodalModels.All.Select(model => model.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (var model in ManagedMultimodalModels.All)
        {
            Assert.StartsWith("2026-", model.ReleaseDate, StringComparison.Ordinal);
            Assert.Matches("^[0-9a-f]{40}$", model.Revision);
            Assert.Equal("modelscope.cn", model.BuildDownloadUri(model.ModelFile).Host);
            Assert.Equal("https", model.BuildDownloadUri(model.ModelFile).Scheme);
            Assert.True(model.ModelFile.Size > 1_000_000_000);
            Assert.True(model.ProjectorFile.Size > 400_000_000);
            Assert.Matches("^[0-9a-f]{64}$", model.ModelFile.Sha256);
            Assert.Matches("^[0-9a-f]{64}$", model.ProjectorFile.Sha256);
            Assert.Equal("Apache-2.0", model.License);
        }
    }

    [Fact]
    public void RecommendedModel_IsNewQwen35AndFitsLightweightTarget()
    {
        var model = ManagedMultimodalModels.Recommended;

        Assert.Equal("qwen35-2b-q4", model.Id);
        Assert.Contains("Qwen3.5", model.DisplayName, StringComparison.Ordinal);
        Assert.True(model.TotalSize < 2L * 1024 * 1024 * 1024);
        Assert.Contains("Q4", model.Quantization, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("auto", "auto")]
    [InlineData("VULKAN", "vulkan")]
    [InlineData("cpu", "cpu")]
    [InlineData("cuda", "auto")]
    public void RuntimeBackend_NormalizesSupportedChoices(string value, string expected)
    {
        Assert.Equal(expected, ManagedRuntimeBackends.Normalize(value));
    }

    [Fact]
    public void Settings_PreserveValidManagedRuntimeAndDisableItForAnotherEndpoint()
    {
        var configured = new AppSettings
        {
            ManagedModelPackageId = ManagedMultimodalModels.Recommended.Id,
            ManagedRuntimeBackend = ManagedRuntimeBackends.Cpu.Id,
            ManagedRuntimeEnabled = true,
            CustomTranslationEndpoint = AppSettings.ManagedModelEndpoint,
            CustomTranslationModel = ManagedMultimodalModels.Recommended.ModelAlias
        }.Normalize();

        Assert.True(configured.ManagedRuntimeEnabled);
        Assert.Equal("cpu", configured.ManagedRuntimeBackend);

        var external = configured with
        {
            CustomTranslationEndpoint = "http://127.0.0.1:8080/v1/chat/completions"
        };
        Assert.False(external.Normalize().ManagedRuntimeEnabled);
    }
}
