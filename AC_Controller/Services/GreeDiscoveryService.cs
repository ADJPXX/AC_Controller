using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AC_Controller.Models;

namespace AC_Controller.Services;

public sealed class GreeDiscoveryService
{
    private static readonly TimeSpan BroadcastWindow = TimeSpan.FromMilliseconds(1400);
    private static readonly TimeSpan DirectScanTimeout = TimeSpan.FromSeconds(2);
    private const string ScanRequest = "{\"t\":\"scan\"}";

    public async Task<IReadOnlyList<GreeDeviceInfo>> ScanAsync(
        string? lastKnownIp = null,
        CancellationToken cancellationToken = default)
    {
        var targets = GetBroadcastTargets().ToArray();
        var scanTasks = targets.Select(target => ScanBroadcastAsync(target, cancellationToken));
        var results = (await Task.WhenAll(scanTasks)).SelectMany(x => x).ToList();

        if (IPAddress.TryParse(lastKnownIp, out var lastIp) && lastIp.AddressFamily == AddressFamily.InterNetwork &&
            results.All(device => !device.IpAddress.Equals(lastIp)))
        {
            var knownDevice = await ScanAddressAsync(lastIp, null, DirectScanTimeout, cancellationToken);
            if (knownDevice is not null)
                results.Add(knownDevice);
        }

        return results
            .GroupBy(device => device.Mac, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(device => device.Mac, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public Task<GreeDeviceInfo?> RefreshDeviceAsync(
        string mac,
        IPAddress lastKnownIp,
        CancellationToken cancellationToken = default)
    {
        return ScanAddressAsync(lastKnownIp, mac, DirectScanTimeout, cancellationToken);
    }

    private static async Task<IReadOnlyList<GreeDeviceInfo>> ScanBroadcastAsync(
        (IPAddress LocalAddress, IPAddress BroadcastAddress) target,
        CancellationToken cancellationToken)
    {
        var results = new List<GreeDeviceInfo>();

        try
        {
            using var client = new UdpClient(new IPEndPoint(target.LocalAddress, 0))
            {
                EnableBroadcast = true
            };

            var bytes = Encoding.UTF8.GetBytes(ScanRequest);
            await client.SendAsync(bytes, bytes.Length, new IPEndPoint(target.BroadcastAddress, GreeResources.Port));

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(BroadcastWindow);

            while (!timeout.IsCancellationRequested)
            {
                UdpReceiveResult response;
                try
                {
                    response = await client.ReceiveAsync(timeout.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var device = TryParseScanResponse(response.Buffer, response.RemoteEndPoint);
                if (device is not null)
                    results.Add(device);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SocketException)
        {
            // An individual network adapter may reject broadcast; other adapters can still find the unit.
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        return results;
    }

    private static async Task<GreeDeviceInfo?> ScanAddressAsync(
        IPAddress address,
        string? requiredMac,
        TimeSpan timeoutDuration,
        CancellationToken cancellationToken)
    {
        using var client = new UdpClient(AddressFamily.InterNetwork);
        var bytes = Encoding.UTF8.GetBytes(ScanRequest);
        await client.SendAsync(bytes, bytes.Length, new IPEndPoint(address, GreeResources.Port));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutDuration);

        while (true)
        {
            UdpReceiveResult response;
            try
            {
                response = await client.ReceiveAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            if (!response.RemoteEndPoint.Address.Equals(address))
                continue;

            var device = TryParseScanResponse(response.Buffer, response.RemoteEndPoint);
            if (device is null)
                continue;

            if (requiredMac is null || string.Equals(device.Mac, NormalizeMac(requiredMac), StringComparison.OrdinalIgnoreCase))
                return device;
        }
    }

    private static GreeDeviceInfo? TryParseScanResponse(byte[] buffer, IPEndPoint remoteEndPoint)
    {
        try
        {
            var packetLength = Array.LastIndexOf(buffer, (byte)'}') + 1;
            if (packetLength <= 0)
                return null;

            using var outerDocument = JsonDocument.Parse(buffer.AsMemory(0, packetLength));
            var outer = outerDocument.RootElement;
            if (!outer.TryGetProperty("pack", out var packElement) || packElement.ValueKind != JsonValueKind.String)
                return null;

            var pack = packElement.GetString();
            if (string.IsNullOrWhiteSpace(pack))
                return null;

            var hasTag = outer.TryGetProperty("tag", out var tagElement) && tagElement.ValueKind == JsonValueKind.String;
            var version = hasTag ? GreeEncryptionVersion.Gcm : GreeEncryptionVersion.Ecb;
            var plaintext = GreeCryptoService.DecryptGeneric(pack, hasTag ? tagElement.GetString() : null, version);

            using var packDocument = JsonDocument.Parse(plaintext);
            var device = packDocument.RootElement;

            var mac = ReadString(device, "mac");
            if (string.IsNullOrWhiteSpace(mac)) mac = ReadString(device, "cid");
            if (string.IsNullOrWhiteSpace(mac)) mac = ReadString(outer, "cid");
            mac = NormalizeMac(mac ?? string.Empty);

            if (!Regex.IsMatch(mac, "^[0-9a-f]{12}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                return null;

            return new GreeDeviceInfo(
                mac,
                remoteEndPoint.Address,
                GreeResources.Port,
                version,
                ReadString(device, "name") ?? ReadString(device, "brand") ?? "Gree",
                ReadString(device, "model") ?? ReadString(device, "mid") ?? string.Empty);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or CryptographicException or
                                   ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static IEnumerable<(IPAddress LocalAddress, IPAddress BroadcastAddress)> GetBroadcastTargets()
    {
        foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (network.OperationalStatus != OperationalStatus.Up ||
                network.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;

            IPInterfaceProperties properties;
            try
            {
                properties = network.GetIPProperties();
            }
            catch (NetworkInformationException)
            {
                continue;
            }

            foreach (var unicast in properties.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask is null ||
                    IPAddress.IsLoopback(unicast.Address) ||
                    unicast.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                    continue;

                var addressBytes = unicast.Address.GetAddressBytes();
                var maskBytes = unicast.IPv4Mask.GetAddressBytes();
                var broadcastBytes = new byte[4];
                for (var i = 0; i < broadcastBytes.Length; i++)
                    broadcastBytes[i] = (byte)(addressBytes[i] | ~maskBytes[i]);

                yield return (unicast.Address, new IPAddress(broadcastBytes));
            }
        }
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    public static string NormalizeMac(string mac)
    {
        return mac.Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Trim()
            .ToLowerInvariant();
    }
}
