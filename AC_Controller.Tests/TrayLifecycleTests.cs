using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Threading;
using AC_Controller.Models;

namespace AC_Controller.Tests;

public sealed class TrayLifecycleTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public Task ClosingTheOnlySelectorUsesExplicitApplicationShutdown() =>
        WpfTestProcess.RunAsync(typeof(TrayLifecycleTests), nameof(ClosingTheOnlySelectorUsesExplicitApplicationShutdown), CheckShutdownAsync);

    private static async Task CheckShutdownAsync()
    {
        var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var app = new App();
                app.InitializeComponent();
                var selector = new DeviceSelectionWindow([]);
                selector.Close();
                var keepsTray = app.Windows.Count == 0 && app.ShutdownMode == ShutdownMode.OnExplicitShutdown;
                // Shut down before dispatching the queued Startup event. This probe never opens the tray or scans.
                app.Shutdown();
                app.Dispatcher.BeginInvokeShutdown(DispatcherPriority.ApplicationIdle);
                Dispatcher.Run();
                finished.SetResult(keepsTray);
            }
            catch (Exception ex)
            {
                finished.SetException(ex);
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(await finished.Task.WaitAsync(Timeout));
    }

    [Theory]
    [InlineData(true, "Desligar", false)]
    [InlineData(false, "Ligar", true)]
    public async Task PowerCommandStillMatchesItsVisibleLabel(bool shownOn, string label, bool expected)
    {
        var actualPower = shownOn;
        var action = App.PowerAction(shownOn,
            _ => { actualPower = true; return Task.CompletedTask; },
            _ => { actualPower = false; return Task.CompletedTask; });
        actualPower = !shownOn; // Simulate a physical remote while the old menu is visible.
        Assert.Equal(label, action.Label);
        await action.Command(default);
        Assert.Equal(expected, actualPower);
    }

    [Fact]
    public async Task CommandWaitsForPollAndPublishesFreshStatusLast()
    {
        var releasePoll = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var enteredPoll = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var displayed = new List<int>();
        var power = 0;
        var commandRan = false;
        var session = new TrayDeviceSession(async token =>
        {
            var snapshot = new GreeStatus { Pow = power };
            enteredPoll.TrySetResult();
            await releasePoll.Task.WaitAsync(token);
            return snapshot;
        }, status => displayed.Add(status.Pow), default);

        var poll = session.RunAsync();
        await enteredPoll.Task.WaitAsync(Timeout);
        var command = session.RunAsync(_ =>
        {
            commandRan = true;
            power = 1;
            return Task.CompletedTask;
        });
        Assert.False(commandRan);
        releasePoll.SetResult();
        await Task.WhenAll(poll, command).WaitAsync(Timeout);
        Assert.Equal(new[] { 0, 1 }, displayed);
    }

    [Fact]
    public async Task RetiredSessionRejectsLateStatusAndQueuedClicksBeforeDraining()
    {
        using var oldLifetime = new CancellationTokenSource();
        var late = new TaskCompletionSource<GreeStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        var displayed = new List<int>();
        var old = new TrayDeviceSession(_ => late.Task, status => displayed.Add(status.Pow), oldLifetime.Token);
        var poll = old.RunAsync();
        var count = 0;
        var queuedClick = old.RunAsync(_ => { count++; return Task.CompletedTask; });
        oldLifetime.Cancel();
        var drain = old.DrainAsync();
        Assert.False(drain.IsCompleted);
        var next = new TrayDeviceSession(_ => Task.FromResult(new GreeStatus { Pow = 1 }),
            status => displayed.Add(status.Pow), default);
        await next.RunAsync();
        late.SetResult(new GreeStatus { Pow = 0 });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => poll.WaitAsync(Timeout));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queuedClick.WaitAsync(Timeout));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => old.RunAsync());
        await drain.WaitAsync(Timeout);
        Assert.Equal(0, count);
        Assert.Equal(new[] { 1 }, displayed);
    }

    [Fact]
    public async Task ShutdownCancelsCurrentCommandAndSkipsFollowupRead()
    {
        using var lifetime = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        var session = new TrayDeviceSession(_ => { reads++; return Task.FromResult(new GreeStatus()); },
            _ => { }, lifetime.Token);
        var command = session.RunAsync(async token =>
        {
            started.SetResult();
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
        });
        await started.Task.WaitAsync(Timeout);
        lifetime.Cancel();
        await session.DrainAsync().WaitAsync(Timeout);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => command);
        Assert.Equal(0, reads);
    }

    [Fact]
    public async Task SimultaneousCallersUseOneDeviceOperationAtATime()
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = 0;
        var published = 0;
        var session = new TrayDeviceSession(async _ =>
        {
            Assert.Equal(1, Interlocked.Increment(ref held));
            await Task.Yield();
            Interlocked.Decrement(ref held);
            return new GreeStatus();
        }, _ => Interlocked.Increment(ref published), default);
        var calls = Enumerable.Range(0, 64).Select(_ => Task.Run(async () =>
        {
            await start.Task;
            await session.RunAsync(async _ =>
            {
                Assert.Equal(1, Interlocked.Increment(ref held));
                await Task.Yield();
                Interlocked.Decrement(ref held);
            });
        })).ToArray();
        start.SetResult();
        await Task.WhenAll(calls).WaitAsync(Timeout);
        await session.DrainAsync().WaitAsync(Timeout);
        Assert.Equal(calls.Length, published);
    }

    [Fact]
    public async Task FaultedOperationReleasesFollowingCommand()
    {
        var session = new TrayDeviceSession(_ => Task.FromResult(new GreeStatus()), _ => { }, default);
        await Assert.ThrowsAsync<IOException>(() => session.RunAsync(_ => throw new IOException()));
        await session.RunAsync().WaitAsync(Timeout);
        await session.DrainAsync().WaitAsync(Timeout);
    }

    [Fact]
    public void ChoiceSurvivesEmptyScanAndBindingRetry()
    {
        var first = Device("001122334455");
        var second = Device("001122334466");
        GreeDeviceInfo[] devices = [first, second];
        var selection = new TraySelection();
        selection.RequestChoice();
        Assert.Throws<IOException>(() => selection.Choose([], first.Mac, _ => second, default));
        Assert.Equal(second, selection.Choose(devices, first.Mac, _ => second, default));
        Assert.Equal(second, selection.Choose(devices, first.Mac, _ => throw new InvalidOperationException(), default));
    }

    [Fact]
    public void CancelDuringDialogPreservesTheNewerSelectionRequest()
    {
        GreeDeviceInfo[] devices = [Device("001122334455"), Device("001122334466")];
        var selection = new TraySelection();
        using var attempt = new CancellationTokenSource();
        Assert.ThrowsAny<OperationCanceledException>(() => selection.Choose(devices, null, _ =>
        {
            selection.RequestChoice();
            attempt.Cancel();
            return null;
        }, attempt.Token));
        Assert.Equal(devices[1], selection.Choose(devices, devices[0].Mac, _ => devices[1], default));
    }

    [Theory]
    [InlineData(0, 0, "Auto")]
    [InlineData(0, 1, "Forte")]
    [InlineData(5, 0, "Alto")]
    public void FanNameRepresentsAutomaticModeAndTurbo(int speed, int turbo, string expected)
    {
        Assert.Equal(expected, App.GetSpeedName(new GreeStatus { WdSpd = speed, Tur = turbo }));
    }

    private static GreeDeviceInfo Device(string mac) =>
        new(mac, IPAddress.Loopback, 7000, GreeEncryptionVersion.Ecb, "Teste", "Teste");
}
