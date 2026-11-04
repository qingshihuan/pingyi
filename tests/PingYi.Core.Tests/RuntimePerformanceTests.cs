using PingYi.Core;

namespace PingYi.Core.Tests;

public class RuntimePerformanceTests
{
    [Fact]
    public void Passive_refresh_expires_and_invalidates_when_settings_change()
    {
        var clock = new TestClock();
        var policy = new PassiveRefreshPolicy(TimeSpan.FromSeconds(30), clock);
        var settings = new AppSettings();
        Assert.True(policy.ShouldRefresh(settings));
        policy.RecordRefresh(settings);
        for (var i = 0; i < 100; i++) Assert.False(policy.ShouldRefresh(settings));
        Assert.True(policy.ShouldRefresh(settings with { TargetLanguage = "en" }));
        clock.Ticks += TimeSpan.FromSeconds(30).Ticks;
        Assert.True(policy.ShouldRefresh(settings));
    }

    private sealed class TestClock : TimeProvider
    {
        public long Ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Ticks;
    }
}
