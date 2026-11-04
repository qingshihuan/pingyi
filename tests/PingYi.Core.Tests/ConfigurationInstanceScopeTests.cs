using PingYi.Core;
namespace PingYi.Core.Tests;

public sealed class ConfigurationInstanceScopeTests
{
    [Fact]
    public void Default_instance_is_unchanged_and_explicit_test_directories_are_isolated()
    {
        Assert.Equal("", ConfigurationInstanceScope.ForDirectory(null));
        var first = Path.Combine(Path.GetTempPath(), "synthetic-one");
        var second = Path.Combine(Path.GetTempPath(), "synthetic-two");
        Assert.NotEqual(ConfigurationInstanceScope.ForDirectory(first), ConfigurationInstanceScope.ForDirectory(second));
        Assert.Equal(ConfigurationInstanceScope.ForDirectory(first), ConfigurationInstanceScope.ForDirectory(first + Path.DirectorySeparatorChar));
        Assert.DoesNotContain(first, ConfigurationInstanceScope.ForDirectory(first));
    }
}
