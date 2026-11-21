using AC_Controller.Models;
using AC_Controller.Services;

namespace AC_Controller;

internal enum PanelCommand { Power, Mode, Temperature, Fan, Swing }

internal readonly record struct PanelAction(PanelCommand Command, int Value)
{
    internal bool IsAvailable(GreeStatus? status, bool busy) =>
        status is not null && !busy && (status.IsPoweredOn || Command == PanelCommand.Power);

    internal Task SendAsync(GreeService service, CancellationToken token) => Command switch
    {
        PanelCommand.Power when Value == 1 => service.TurnOnAsync(token),
        PanelCommand.Power when Value == 0 => service.TurnOffAsync(token),
        PanelCommand.Mode => service.SetModeAsync(Value, token),
        PanelCommand.Temperature => service.SetTemperatureAsync(Value, token),
        PanelCommand.Fan => service.SetWindSpeedAsync(Value, token),
        PanelCommand.Swing => service.SetFinDirectionAsync(Value, token),
        _ => throw new ArgumentOutOfRangeException(nameof(Command), "Comando ou valor de energia inválido.")
    };
}

internal sealed record PanelState(
    GreeStatus? Status, string? DeviceName, string? DeviceAddress,
    string ConnectionMessage, bool Searching, bool Busy, string? CommandFailure = null);
