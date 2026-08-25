using ScopeDomeWatchdog.Core.Models;
using ScopeDomeWatchdog.Core.Services;
using Xunit;

namespace ScopeDomeWatchdog.Core.Tests;

public sealed class WatchdogTriggerTests
{
    [Fact]
    public async Task FailedMonitorPingInvokesRestartHandlerIndependentlyOfShellyIp()
    {
        var config = new WatchdogConfig
        {
            MonitorIp = "192.0.2.1",
            PlugIp = "127.0.0.1",
            FailsToTrigger = 1,
            PingTimeoutMs = 100,
            PingIntervalSec = 1,
            CooldownSeconds = 600
        };
        var requested = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var runner = new WatchdogRunner(config);
        runner.SetRestartHandler((reason, _) =>
        {
            requested.TrySetResult(reason);
            return Task.FromResult(true);
        });

        runner.Start();
        var reason = await requested.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Contains(config.MonitorIp, reason, StringComparison.Ordinal);
        Assert.DoesNotContain(config.PlugIp, reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StopThenStartDetectsASecondOutage()
    {
        var config = new WatchdogConfig
        {
            MonitorIp = "192.0.2.1",
            PlugIp = "127.0.0.1",
            FailsToTrigger = 1,
            PingTimeoutMs = 100,
            PingIntervalSec = 1,
            CooldownSeconds = 0
        };
        var firstRequest = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRequest = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var runner = new WatchdogRunner(config);
        runner.SetRestartHandler((_, _) =>
        {
            firstRequest.TrySetResult();
            return Task.FromResult(true);
        });

        runner.Start();
        await firstRequest.Task.WaitAsync(TimeSpan.FromSeconds(5));
        runner.Stop();

        runner.SetRestartHandler((_, _) =>
        {
            secondRequest.TrySetResult();
            return Task.FromResult(true);
        });
        runner.Start();

        await secondRequest.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(runner.IsRunning);
    }
}
