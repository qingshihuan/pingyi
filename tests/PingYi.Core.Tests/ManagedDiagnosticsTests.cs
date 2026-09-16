using System.Collections.Concurrent;
using PingYi.Infrastructure;

namespace PingYi.Core.Tests;

public class ManagedDiagnosticsTests
{
    [Fact]
    public async Task Long_native_output_retains_no_raw_prompt_or_secret()
    {
        var messages = new ConcurrentQueue<string>();
        var raw = new string('x', 2_000_000) + " out of memory private-image secret-key";
        await ManagedServerDiagnostics.DrainAsync(new StringReader(raw), messages);
        Assert.Single(messages);
        Assert.Contains("内存", messages.Single());
        Assert.DoesNotContain("secret", messages.Single());
        Assert.DoesNotContain("private", messages.Single());
    }
}
