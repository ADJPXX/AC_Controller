using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using AC_Controller.Models;

namespace AC_Controller.Services;

internal interface IGreeScanProbes
{
    IEnumerable<(IPAddress LocalAddress, IPAddress BroadcastAddress)> GetBroadcastTargets();
    Task<IReadOnlyList<GreeDeviceInfo>> ScanBroadcastAsync(
        (IPAddress LocalAddress, IPAddress BroadcastAddress) target, CancellationToken cancellationToken);
    Task<GreeDeviceInfo?> ScanAddressAsync(IPAddress address, string? requiredMac, CancellationToken cancellationToken);
}

public sealed class GreeDiscoveryService
{
    private readonly IGreeScanProbes _probes;

    public GreeDiscoveryService() : this(new UdpScanProbes()) { }

    internal GreeDiscoveryService(IGreeScanProbes probes) => _probes = probes;

    public async Task<IReadOnlyList<GreeDeviceInfo>> ScanAsync(
        string? lastKnownIp = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var errors = new List<Exception>();
        var scans = new List<Task<(IReadOnlyList<GreeDeviceInfo> Devices, Exception? Error)>>();
        try
        {
            foreach (var target in _probes.GetBroadcastTargets().Distinct())
                scans.Add(ScanAdapterAsync(target, cancellationToken));
        }
        catch (NetworkInformationException ex)
        {
            errors.Add(ex);
        }

        var devices = new List<GreeDeviceInfo>();
        foreach (var (found, error) in await Task.WhenAll(scans).ConfigureAwait(false))
        {
            devices.AddRange(found);
            if (error is not null) errors.Add(error);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (IPAddress.TryParse(lastKnownIp, out var lastIp) && lastIp.AddressFamily == AddressFamily.InterNetwork &&
            devices.All(device => !device.IpAddress.Equals(lastIp)))
        {
            try
            {
                var known = await _probes.ScanAddressAsync(lastIp, null, cancellationToken).ConfigureAwait(false);
                if (known is not null) devices.Add(known);
            }
            catch (SocketException ex)
            {
                errors.Add(ex);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (devices.Count == 0 && errors.Count > 0)
            throw new IOException("A busca de aparelhos encontrou falhas de rede.", new AggregateException(errors));

        return devices.GroupBy(device => device.Mac, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(device => device.Mac, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<(IReadOnlyList<GreeDeviceInfo> Devices, Exception? Error)> ScanAdapterAsync(
        (IPAddress LocalAddress, IPAddress BroadcastAddress) target, CancellationToken cancellationToken)
    {
        try
        {
            return (await _probes.ScanBroadcastAsync(target, cancellationToken).ConfigureAwait(false), null);
        }
        catch (SocketException ex)
        {
            return (Array.Empty<GreeDeviceInfo>(), ex);
        }
    }

    public Task<GreeDeviceInfo?> RefreshDeviceAsync(
        string mac,
        IPAddress lastKnownIp,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(lastKnownIp);
        if (!TryNormalizeMac(mac, out var normalized))
            throw new ArgumentException("Endereço MAC inválido.", nameof(mac));
        return _probes.ScanAddressAsync(lastKnownIp, normalized, cancellationToken);
    }

    public static string NormalizeMac(string mac)
    {
        ArgumentNullException.ThrowIfNull(mac);
        return mac.Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Trim().ToLowerInvariant();
    }

    internal static bool TryNormalizeMac(string? mac, out string normalized)
    {
        normalized = mac is null ? string.Empty : NormalizeMac(mac);
        return normalized.Length == 12 && normalized.All(char.IsAsciiHexDigit);
    }
}

internal sealed class UdpScanProbes(Action<UdpClient>? configureBroadcast = null) : IGreeScanProbes
{
    private readonly Action<UdpClient> _configureBroadcast = configureBroadcast ?? (client => client.EnableBroadcast = true);
    private static readonly TimeSpan BroadcastWindow = TimeSpan.FromMilliseconds(1400);
    private static readonly TimeSpan DirectScanTimeout = TimeSpan.FromSeconds(2);

    public async Task<IReadOnlyList<GreeDeviceInfo>> ScanBroadcastAsync(
        (IPAddress LocalAddress, IPAddress BroadcastAddress) target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(BroadcastWindow);
        using var client = new UdpClient(new IPEndPoint(target.LocalAddress, 0));
        _configureBroadcast(client);
        var results = new Dictionary<string, GreeDeviceInfo>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var endpoint = new IPEndPoint(target.BroadcastAddress, GreeResources.Port);
            await client.SendAsync("{\"t\":\"scan\"}"u8.ToArray(), endpoint, deadline.Token).ConfigureAwait(false);
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                var response = await client.ReceiveAsync(deadline.Token).ConfigureAwait(false);
                CollectScanResponse(results, response);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            // The listen window ended. Deliver the devices collected by this adapter.
        }
        catch (SocketException) when (results.Count > 0)
        {
            // The adapter disappeared after it had already discovered at least one unit.
        }
        cancellationToken.ThrowIfCancellationRequested();
        return results.Values.ToArray();
    }

    public async Task<GreeDeviceInfo?> ScanAddressAsync(
        IPAddress address, string? requiredMac, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(DirectScanTimeout);
        using var client = new UdpClient(AddressFamily.InterNetwork);
        try
        {
            var endpoint = new IPEndPoint(address, GreeResources.Port);
            await client.SendAsync("{\"t\":\"scan\"}"u8.ToArray(), endpoint, deadline.Token).ConfigureAwait(false);
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                var response = await client.ReceiveAsync(deadline.Token).ConfigureAwait(false);
                if (!response.RemoteEndPoint.Address.Equals(address)) continue;

                var device = TryParseScanResponse(response.Buffer, response.RemoteEndPoint);
                if (device is not null &&
                    (requiredMac is null || string.Equals(device.Mac, requiredMac, StringComparison.OrdinalIgnoreCase)))
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    return device;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            return null;
        }
    }

    internal static void CollectScanResponse(Dictionary<string, GreeDeviceInfo> results, UdpReceiveResult response)
    {
        var device = TryParseScanResponse(response.Buffer, response.RemoteEndPoint);
        if (device is not null) results.TryAdd(device.Mac, device);
    }

    internal static GreeDeviceInfo? TryParseScanResponse(byte[] buffer, IPEndPoint remoteEndPoint)
    {
        try
        {
            var packetLength = Array.LastIndexOf(buffer, (byte)'}') + 1;
            if (packetLength <= 0) return null;

            using var outerDocument = JsonDocument.Parse(buffer.AsMemory(0, packetLength));
            var outer = outerDocument.RootElement;
            if (outer.ValueKind != JsonValueKind.Object ||
                !outer.TryGetProperty("pack", out var packElement) || packElement.ValueKind != JsonValueKind.String)
                return null;
            var pack = packElement.GetString();
            if (string.IsNullOrWhiteSpace(pack)) return null;

            var hasTag = outer.TryGetProperty("tag", out var tagElement) && tagElement.ValueKind != JsonValueKind.Null;
            if (hasTag && tagElement.ValueKind != JsonValueKind.String) return null;

            var version = hasTag ? GreeEncryptionVersion.Gcm : GreeEncryptionVersion.Ecb;
            var plaintext = GreeCryptoService.DecryptGeneric(pack, hasTag ? tagElement.GetString() : null, version);
            using var packDocument = JsonDocument.Parse(plaintext);
            var device = packDocument.RootElement;
            if (device.ValueKind != JsonValueKind.Object) return null;
            var mac = ReadString(device, "mac");
            if (string.IsNullOrWhiteSpace(mac)) mac = ReadString(device, "cid");
            if (string.IsNullOrWhiteSpace(mac)) mac = ReadString(outer, "cid");
            if (!GreeDiscoveryService.TryNormalizeMac(mac, out var normalized)) return null;

            return new GreeDeviceInfo(normalized, remoteEndPoint.Address, GreeResources.Port, version,
                ReadString(device, "name") ?? ReadString(device, "brand") ?? "Gree",
                ReadString(device, "model") ?? ReadString(device, "mid") ?? string.Empty);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or CryptographicException or
                                   ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    public IEnumerable<(IPAddress LocalAddress, IPAddress BroadcastAddress)> GetBroadcastTargets()
    {
        var targets = new List<(IPAddress, IPAddress)>();
        NetworkInformationException? lastError = null;
        foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
        {
            try
            {
                if (network.OperationalStatus != OperationalStatus.Up ||
                    network.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    continue;
                foreach (var unicast in network.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask is null ||
                        IPAddress.IsLoopback(unicast.Address))
                        continue;

                    var addressBytes = unicast.Address.GetAddressBytes();
                    if (addressBytes[0] == 169 && addressBytes[1] == 254) continue;
                    var maskBytes = unicast.IPv4Mask.GetAddressBytes();
                    var broadcastBytes = new byte[4];
                    for (var i = 0; i < broadcastBytes.Length; i++)
                        broadcastBytes[i] = (byte)(addressBytes[i] | ~maskBytes[i]);
                    targets.Add((unicast.Address, new IPAddress(broadcastBytes)));
                }
            }
            catch (NetworkInformationException ex)
            {
                lastError = ex;
            }
        }
        if (targets.Count == 0 && lastError is not null)
            throw lastError;
        return targets;
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
}
