using PingYi.Core;
namespace PingYi.Core.Tests;

public sealed class ModeReadinessTests
{
    [Theory]
    [InlineData(true, true, ModeReadinessState.Available)]
    [InlineData(true, false, ModeReadinessState.NeedsAttention)]
    [InlineData(false, true, ModeReadinessState.NeedsAttention)]
    [InlineData(false, false, ModeReadinessState.NeedsAttention)]
    [InlineData(null, true, ModeReadinessState.Unknown)]
    [InlineData(true, null, ModeReadinessState.Unknown)]
    public void Lightweight_requires_both_local_components(bool? ocr, bool? translator, ModeReadinessState state) =>
        Assert.Equal(state, ModeReadinessPolicy.Lightweight(ocr, translator).State);

    [Theory]
    [InlineData(false, true, true, true, false, null, ModeReadinessState.Unconfigured)]
    [InlineData(true, true, true, true, false, null, ModeReadinessState.OnDemand)]
    [InlineData(true, true, true, true, true, null, ModeReadinessState.Available)]
    [InlineData(true, true, false, true, false, null, ModeReadinessState.NeedsAttention)]
    [InlineData(true, true, true, false, false, null, ModeReadinessState.NeedsAttention)]
    [InlineData(true, false, false, false, false, true, ModeReadinessState.Unverified)]
    [InlineData(true, false, false, false, false, false, ModeReadinessState.NeedsAttention)]
    public void Basic_distinguishes_missing_files_idle_models_and_external_service(bool configured, bool managed,
        bool files, bool runtime, bool running, bool? connected, ModeReadinessState state) =>
        Assert.Equal(state, ModeReadinessPolicy.Basic(new(configured, managed, files, runtime, running, connected)).State);

    [Fact]
    public void Default_endpoint_and_cloud_credentials_are_not_evidence_of_a_working_model()
    {
        Assert.False(ModeReadinessPolicy.HasLocalConfiguration(new AppSettings()));
        Assert.Equal(ModeReadinessState.Unconfigured, ModeReadinessPolicy.Cloud(new(true, false, false, false, false, false, false)).State);
        Assert.Equal(ModeReadinessState.Unverified, ModeReadinessPolicy.Cloud(new(true, true, false, false, false, false, false)).State);
        Assert.Equal(ModeReadinessState.Unverified, ModeReadinessPolicy.Cloud(new(true, false, true, true, true, true, false)).State);
        Assert.Equal(ModeReadinessState.NeedsAttention, ModeReadinessPolicy.Cloud(new(true, false, true, false, true, true, false)).State);
        Assert.Equal(ModeReadinessState.Unknown, ModeReadinessPolicy.Cloud(new(false, false, false, false, false, false, false)).State);
    }
    [Theory]
    [InlineData("https://example.com/v1", true)]
    [InlineData("http://example.com/v1", false)]
    [InlineData("http://127.0.0.1:8080/v1", false)]
    [InlineData("https://secret@example.com/v1", false)]
    [InlineData("not a url", false)]
    public void Remote_status_requires_a_secure_nonloopback_endpoint(string endpoint, bool configured)
    {
        var settings = new AppSettings { CustomTranslationEndpoint = endpoint, InitialSetupCompleted = true };
        Assert.Equal(configured, ModeReadinessPolicy.HasRemoteEndpoint(settings));
        if (configured) Assert.False(ModeReadinessPolicy.HasLocalConfiguration(settings));
    }
    [Fact]
    public void Pending_states_are_not_green()
    {
        foreach (var state in new[] { ModeReadinessState.Unknown, ModeReadinessState.Unconfigured,
            ModeReadinessState.NeedsAttention, ModeReadinessState.Unverified })
            Assert.False(new ModeReadiness(state, "test").IsReady);
    }
}
