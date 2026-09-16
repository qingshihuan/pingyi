using System.Net;
using System.Text;
using PingYi.Infrastructure;

namespace PingYi.Core.Tests;

public class ManagedReadinessTests
{
    private static readonly Uri Health = new("http://127.0.0.1:18080/health");
    private static readonly Uri Models = new("http://127.0.0.1:18080/v1/models");

    [Fact]
    public async Task Slow_probe_is_retried_instead_of_being_reported_as_the_whole_loading_timeout()
    {
        var calls = 0;
        using var client = new HttpClient(new Handler(async (request, token) =>
        {
            if (Interlocked.Increment(ref calls) == 1) await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Ready(request);
        })) { Timeout = Timeout.InfiniteTimeSpan };
        await ManagedRuntimeReadiness.WaitAsync(client, Health, Models, "vision", () => null,
            TimeSpan.FromSeconds(3), default, probeTimeout: TimeSpan.FromMilliseconds(40), pollInterval: TimeSpan.FromMilliseconds(10));
        Assert.True(calls >= 3);
    }

    [Fact]
    public async Task Loading_503_and_wrong_alias_do_not_report_ready()
    {
        var healthCalls = 0;
        var modelCalls = 0;
        using var client = new HttpClient(new Handler((request, _) =>
        {
            if (request.RequestUri == Health)
            {
                healthCalls++;
                return Task.FromResult(healthCalls == 1 ? Json("{\"error\":\"loading\"}", HttpStatusCode.ServiceUnavailable) : Ready(request));
            }
            modelCalls++;
            return Task.FromResult(modelCalls == 1 ? Json("{\"data\":[{\"id\":\"wrong-model\"}]}") : Ready(request));
        }));
        await ManagedRuntimeReadiness.WaitAsync(client, Health, Models, "vision", () => null,
            TimeSpan.FromSeconds(3), default, pollInterval: TimeSpan.FromMilliseconds(10));
        Assert.Equal(3, healthCalls);
        Assert.Equal(2, modelCalls);
    }

    [Fact]
    public async Task Overall_deadline_remains_bounded_when_every_probe_times_out()
    {
        using var client = new HttpClient(new Handler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException();
        })) { Timeout = Timeout.InfiniteTimeSpan };
        await Assert.ThrowsAsync<TimeoutException>(() => ManagedRuntimeReadiness.WaitAsync(client, Health, Models, "vision",
            () => null, TimeSpan.FromMilliseconds(180), default,
            probeTimeout: TimeSpan.FromMilliseconds(30), pollInterval: TimeSpan.FromMilliseconds(10)));
    }

    [Fact]
    public async Task User_cancellation_is_not_translated_to_a_timeout()
    {
        using var cts = new CancellationTokenSource();
        using var client = new HttpClient(new Handler(async (_, token) =>
        {
            cts.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException();
        }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ManagedRuntimeReadiness.WaitAsync(client,
            Health, Models, "vision", () => null, TimeSpan.FromSeconds(2), cts.Token));
    }

    [Fact]
    public async Task Early_process_exit_does_not_wait_for_the_deadline()
    {
        using var client = new HttpClient(new Handler((_, _) => throw new InvalidOperationException("Must not probe")));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => ManagedRuntimeReadiness.WaitAsync(client,
            Health, Models, "vision", () => 42, TimeSpan.FromMinutes(3), default));
        Assert.Contains("42", error.Message);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"status\":\"loading\"}")]
    [InlineData("not JSON")]
    public async Task Malformed_or_nonready_health_cannot_be_treated_as_success(string body)
    {
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(Json(body))));
        await Assert.ThrowsAsync<TimeoutException>(() => ManagedRuntimeReadiness.WaitAsync(client,
            Health, Models, "vision", () => null, TimeSpan.FromMilliseconds(80), default,
            pollInterval: TimeSpan.FromMilliseconds(10)));
    }

    [Fact]
    public void Capture_budget_includes_both_auto_backends_and_verification()
    {
        Assert.True(ManagedRuntimeReadiness.OperationTimeout >
            ManagedRuntimeReadiness.VulkanTimeout + ManagedRuntimeReadiness.CpuTimeout);
    }

    private static HttpResponseMessage Ready(HttpRequestMessage request) => Json(request.RequestUri == Health
        ? "{\"status\":\"ok\"}" : "{\"data\":[{\"id\":\"vision\"}]}");
    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
}
