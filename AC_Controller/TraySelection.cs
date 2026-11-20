using System.IO;
using AC_Controller.Models;
using AC_Controller.Services;

namespace AC_Controller;

// All calls are made on the WPF Dispatcher, including clicks during a modal dialog.
internal sealed class TraySelection
{
    private bool _requested;
    private bool _declined;
    private string? _chosenMac;

    public void RequestChoice()
    {
        _requested = true;
        _declined = false;
    }

    public GreeDeviceInfo Choose(
        IReadOnlyList<GreeDeviceInfo> devices, string? preferredMac,
        Func<IReadOnlyList<GreeDeviceInfo>, GreeDeviceInfo?> show, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (devices.Count == 0)
            throw new IOException("Nenhum aparelho Gree foi encontrado. Confira o Wi-Fi e tente novamente.");

        preferredMac = _chosenMac ?? preferredMac;
        if (!_requested && !string.IsNullOrWhiteSpace(preferredMac))
        {
            var mac = GreeDiscoveryService.NormalizeMac(preferredMac);
            return devices.FirstOrDefault(device => string.Equals(device.Mac, mac, StringComparison.OrdinalIgnoreCase))
                ?? throw new IOException($"O aparelho salvo ({mac}) não foi encontrado na rede local.");
        }
        if (devices.Count == 1)
        {
            _requested = false;
            _declined = false;
            _chosenMac = devices[0].Mac;
            return devices[0];
        }
        if (_declined)
            throw new IOException("Há vários aparelhos na rede. Use ‘Selecionar aparelho’ no menu do AC Controller.");

        var selected = show(devices);
        token.ThrowIfCancellationRequested();
        _requested = false;
        _declined = selected is null;
        if (selected is not null) _chosenMac = selected.Mac;
        return selected ?? throw new IOException("Seleção cancelada. Use ‘Selecionar aparelho’ no menu para escolher uma unidade.");
    }
}
