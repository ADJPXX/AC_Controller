using System.Net;
using System.Text.Json;
using AC_Controller.Models;
using AC_Controller.Services;
using NSubstitute;

namespace AC_Controller.Tests;

public sealed class PanelActionTests
{
    [Theory]
    [InlineData((int)PanelCommand.Power, 0, "Pow", "0")]
    [InlineData((int)PanelCommand.Power, 1, "Pow", "1")]
    [InlineData((int)PanelCommand.Mode, 0, "Mod", "0")]
    [InlineData((int)PanelCommand.Mode, 2, "Mod", "2")]
    [InlineData((int)PanelCommand.Mode, 3, "Mod", "3")]
    [InlineData((int)PanelCommand.Mode, 4, "Mod", "4")]
    [InlineData((int)PanelCommand.Temperature, 25, "SetTem", "25")]
    [InlineData((int)PanelCommand.Fan, 6, "WdSpd,Tur", "1,1")]
    [InlineData((int)PanelCommand.Swing, 1, "SwUpDn", "1")]
    public async Task DisplayedActionSendsItsAbsoluteValueThroughTheSelectedSession(
        int command, int value, string options, string values)
    {
        var session = new GreeSession("001122334455", IPAddress.Loopback, 7000, GreeEncryptionVersion.Gcm, "test-only-key-16");
        using var lifetime = new CancellationTokenSource();
        var transport = Substitute.For<IGreeTransport>();
        var reply = GreeCryptoService.EncryptDevice("""{"t":"res","r":200}""", session);
        transport.SendAsync(Arg.Any<string>(), session.IpAddress, session.Port, lifetime.Token)
            .Returns(Task.FromResult(JsonSerializer.Serialize(new { pack = reply.Pack, tag = reply.Tag })));

        await new PanelAction((PanelCommand)command, value).SendAsync(new GreeService(session, transport), lifetime.Token);

        var call = Assert.Single(transport.ReceivedCalls());
        using var wire = JsonDocument.Parse((string)call.GetArguments()[0]!);
        var envelope = wire.RootElement;
        var payload = GreeCryptoService.DecryptDevice(envelope.GetProperty("pack").GetString()!,
            envelope.GetProperty("tag").GetString(), session);
        using var document = JsonDocument.Parse(payload);
        var sent = document.RootElement;
        Assert.Equal(options, string.Join(",", sent.GetProperty("opt").EnumerateArray().Select(field => field.GetString())));
        Assert.Equal(values, string.Join(",", sent.GetProperty("p").EnumerateArray().Select(field => field.GetInt32())));
    }

    [Fact]
    public void PanelRejectsCommandsDuringDisconnectPowerOffAndOutstandingWork()
    {
        var off = new GreeStatus { Pow = 0 };
        var on = new GreeStatus { Pow = 1 };
        foreach (var command in Enum.GetValues<PanelCommand>())
        {
            var action = new PanelAction(command, 1);
            Assert.False(action.IsAvailable(null, false));
            Assert.False(action.IsAvailable(on, true));
            Assert.Equal(command == PanelCommand.Power, action.IsAvailable(off, false));
            Assert.True(action.IsAvailable(on, false));
        }
    }
}
