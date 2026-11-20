using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using AC_Controller.Models;
using AC_Controller.Services;
using NSubstitute;

namespace AC_Controller.Tests;

public sealed class GreeServiceTests
{
    private static readonly GreeSession Session = new(
        "001122334455", IPAddress.Loopback, 7000, GreeEncryptionVersion.Gcm, "test-only-key-16");
    private readonly IGreeTransport _transport = Substitute.For<IGreeTransport>();
    private GreeService Service => new(Session, _transport);

    private static string Reply(string json)
    {
        var encrypted = GreeCryptoService.EncryptDevice(json, Session);
        return JsonSerializer.Serialize(new { pack = encrypted.Pack, tag = encrypted.Tag });
    }

    private void RespondWith(string json) =>
        _transport.SendAsync(Arg.Any<string>(), Session.IpAddress, Session.Port, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Reply(json)));

    [Fact]
    public async Task SimultaneousFanChoicesEachUseOneCompleteDatagram()
    {
        var sent = new ConcurrentBag<string>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ack = Reply("""{"t":"res","r":200}""");
        _transport.SendAsync(Arg.Any<string>(), Session.IpAddress, Session.Port, Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                using var wire = JsonDocument.Parse(call.Arg<string>());
                var envelope = wire.RootElement;
                sent.Add(GreeCryptoService.DecryptDevice(envelope.GetProperty("pack").GetString()!,
                    envelope.GetProperty("tag").GetString(), Session));
                await release.Task.WaitAsync(TimeSpan.FromSeconds(5));
                return ack;
            });
        var service = Service;
        var choices = Enumerable.Range(0, 48).Select(i => service.SetWindSpeedAsync(i % 2 == 0 ? 6 : 4)).ToArray();
        release.SetResult();
        await Task.WhenAll(choices).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(48, sent.Count);
        var settings = sent.Select(DecodeFanChoice).ToArray();
        Assert.Equal(24, settings.Count(choice => choice == (1, 1)));
        Assert.Equal(24, settings.Count(choice => choice == (4, 0)));
    }

    private static (int Speed, int Turbo) DecodeFanChoice(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        var options = doc.RootElement.GetProperty("opt").EnumerateArray().Select(x => x.GetString()!).ToArray();
        var values = doc.RootElement.GetProperty("p").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        Assert.Equal(2, options.Length);
        Assert.Equal(2, values.Length);
        var fields = options.Zip(values).ToDictionary(pair => pair.First, pair => pair.Second);
        return (fields["WdSpd"], fields["Tur"]);
    }

    [Fact]
    public async Task CancelledCommandsAndStatusNeverReachTransport()
    {
        var token = new CancellationToken(canceled: true);
        var service = Service;
        Func<Task>[] actions = [
            () => service.TurnOnAsync(token), () => service.TurnOffAsync(token),
            () => service.SetWindSpeedAsync(3, token), () => service.SetFinDirectionAsync(2, token),
            () => service.SetTemperatureAsync(24, token), () => service.SetModeAsync(1, token),
            () => service.GetStatusAsync(token)
        ];
        foreach (var action in actions)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(action);
        Assert.Empty(_transport.ReceivedCalls());
    }

    [Fact]
    public async Task CancellationDuringCommandReachesPendingTransport()
    {
        using var stop = new CancellationTokenSource();
        _transport.SendAsync(Arg.Any<string>(), Session.IpAddress, Session.Port, Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, call.Arg<CancellationToken>());
                return string.Empty;
            });
        var command = Service.TurnOnAsync(stop.Token);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => command.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Theory]
    [InlineData("""{"cols":[null,"Pow","Mod","SetTem","WdSpd"],"dat":[0,1,20,3]}""")]
    [InlineData("""{"cols":["Pow","Pow","Mod","SetTem","WdSpd"],"dat":[0,1,1,20,3]}""")]
    [InlineData("""{"cols":["Pow","","Mod","SetTem","WdSpd"],"dat":[0,0,1,20,3]}""")]
    [InlineData("""{"cols":["Pow","Mod","SetTem","WdSpd"],"dat":[0,1,"20",2]}""")]
    [InlineData("""{"cols":[],"dat":[]}""")]
    [InlineData("""{"cols":null,"dat":null}""")]
    public async Task MalformedStatusCannotShiftOrOverwriteAField(string json)
    {
        RespondWith(json);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service.GetStatusAsync());
    }

    [Theory]
    [InlineData("""{"t":"res","cols":["Pow","Mod","SetTem","WdSpd"],"dat":[1,1,20,3]}""")]
    [InlineData("""{"mac":"001122334466","cols":["Pow","Mod","SetTem","WdSpd"],"dat":[1,1,20,3]}""")]
    [InlineData("""{"r":404,"cols":["Pow","Mod","SetTem","WdSpd"],"dat":[1,1,20,3]}""")]
    public async Task StatusMustMatchDeviceOperationAndResult(string json)
    {
        RespondWith(json);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service.GetStatusAsync());
    }

    [Theory]
    [InlineData("""{"t":"dat","r":200}""")]
    [InlineData("""{"r":200,"mac":null}""")]
    [InlineData("""{"r":"200"}""")]
    [InlineData("""{"t":"res"}""")]
    [InlineData("""{"t":"res","r":500}""")]
    public async Task CommandRejectsUnrelatedOrInvalidAcknowledgement(string json)
    {
        RespondWith(json);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service.TurnOffAsync());
    }

    [Theory]
    [InlineData("""{"cols":["Mod","WdSpd","SetTem","Pow"],"dat":[4,0,22,1]}""")]
    [InlineData("""{"t":"dat","mac":"00:11:22:33:44:55","r":200,"cols":["Mod","WdSpd","SetTem","Pow"],"dat":[4,0,22,1]}""")]
    public async Task CompatibleStatusAcceptsReorderingAndOptionalFields(string json)
    {
        RespondWith(json);
        var status = await Service.GetStatusAsync();
        Assert.True(status.IsPoweredOn);
        Assert.Equal(22, status.SetTem);
        Assert.Equal(4, status.Mod);
        Assert.Equal(0, status.Tur);
    }

    [Fact]
    public async Task InvalidPowerCannotBeDisplayedAsOff()
    {
        RespondWith("""{"cols":["Pow","Mod","SetTem","WdSpd"],"dat":[-1,1,20,3]}""");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service.GetStatusAsync());
    }

    [Fact]
    public async Task InvalidSettingsAreRejectedBeforeNetworkUse()
    {
        var service = Service;
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SetFinDirectionAsync(0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SetTemperatureAsync(31));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SetWindSpeedAsync(7));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SetModeAsync(-1));
        Assert.Empty(_transport.ReceivedCalls());
    }
}
