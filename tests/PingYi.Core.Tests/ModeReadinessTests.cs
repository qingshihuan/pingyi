using PingYi.Core;
namespace PingYi.Core.Tests;

public sealed class ModeReadinessTests
{
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
    public void Pending_states_are_not_green()
    {
        foreach (var state in new[] { ModeReadinessState.Unknown, ModeReadinessState.Unconfigured,
            ModeReadinessState.NeedsAttention, ModeReadinessState.Unverified })
            Assert.False(new ModeReadiness(state, "test").IsReady);
    }
}
