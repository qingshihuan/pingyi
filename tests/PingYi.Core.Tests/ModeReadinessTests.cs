using PingYi.Core;
namespace PingYi.Core.Tests;

public sealed class ModeReadinessTests
{
    [Theory]
    [InlineData(false, false, ModeReadinessState.Unverified)]
    [InlineData(true, false, ModeReadinessState.Unverified)]
    [InlineData(false, true, ModeReadinessState.Unverified)]
    [InlineData(true, true, ModeReadinessState.Verified)]
    public void Saved_capability_evidence_is_reported_without_claiming_live_availability(bool text, bool vision, ModeReadinessState expected)
    {
        var profile = new ModelServiceProfile { Id = "synthetic-api", Endpoint = "https://example.test/v1",
            DefaultModel = "multimodal", Models = [new("multimodal", text, vision)] };
        var settings = new AppSettings { ModelConfiguration = new() { Services = [profile],
            CurrentModel = new(profile.Id, "multimodal") } };
        var api = ModeReadinessPolicy.Api(settings);
        Assert.Equal(expected, api.State);
        Assert.False(api.IsReady);
        var current = ModeReadinessPolicy.Current(settings, ModeReadiness.Unknown);
        Assert.Equal(text && vision ? ModeReadinessState.Verified : ModeReadinessState.Unconfigured, current.State);
        Assert.False(current.IsReady);
    }

    [Fact]
    public void Managed_current_model_tracks_the_local_process_state()
    {
        var id = ManagedMultimodalModels.Recommended.Id;
        var settings = new AppSettings { ManagedModelPackageId = id, ManagedRuntimeEnabled = true,
            ModelConfiguration = new() { CurrentModel = new(ModelConfiguration.Managed, id), VerifiedManagedModelId = id } };
        foreach (var local in new[] { new ModeReadiness(ModeReadinessState.OnDemand, "basic-on-demand"),
            new ModeReadiness(ModeReadinessState.Available, "basic-running") })
            Assert.Equal(local, ModeReadinessPolicy.Current(settings, local));
        Assert.Equal(ModeReadinessState.Unconfigured, ModeReadinessPolicy.Api(settings).State);
    }

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
    public void External_local_api_is_an_api_profile_and_does_not_claim_built_in_deployment()
    {
        var settings = new AppSettings { ModelConfiguration = new() { LocalServices = [new()] } };
        Assert.True(ModeReadinessPolicy.HasApiConfiguration(settings));
        Assert.False(ModeReadinessPolicy.HasLocalConfiguration(settings));
    }

    [Fact]
    public void Pending_states_are_not_green()
    {
        foreach (var state in new[] { ModeReadinessState.Unknown, ModeReadinessState.Unconfigured,
            ModeReadinessState.NeedsAttention, ModeReadinessState.Unverified })
            Assert.False(new ModeReadiness(state, "test").IsReady);
    }
}
