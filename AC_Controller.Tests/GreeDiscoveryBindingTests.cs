using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AC_Controller.Models;
using AC_Controller.Services;
using Xunit;

namespace AC_Controller.Tests;

public sealed class GreeDiscoveryBindingTests
{
    private const string Mac = "001122334455";
    private static readonly GreeDeviceInfo Sample = new(Mac, IPAddress.Loopback, 7000, GreeEncryptionVersion.Ecb, "Sala", "Teste");
    private static readonly (IPAddress, IPAddress) FirstTarget = (IPAddress.Loopback, IPAddress.Loopback);

    [Fact]
    public async Task CancelledScanStopsBeforeEnumeratingOrOpeningSockets()
    {
        var cancelled = new CancellationToken(canceled: true);
        var probes = new ProbeStub { Targets = () => throw new InvalidOperationException("Enumeração indevida") };
        var service = new GreeDiscoveryService(probes);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ScanAsync("127.0.0.1", cancelled));
        var udp = new UdpScanProbes();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => udp.ScanBroadcastAsync(FirstTarget, cancelled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => udp.ScanAddressAsync(IPAddress.Loopback, null, cancelled));
    }

    [Fact]
    public async Task FailedBroadcastSetupClosesTheSocketBeforeReturning()
    {
        SafeSocketHandle? handle = null;
        var probes = new UdpScanProbes(client =>
        {
            handle = client.Client.SafeHandle;
            throw new SocketException((int)SocketError.OperationNotSupported);
        });
        await Assert.ThrowsAsync<SocketException>(() => probes.ScanBroadcastAsync(FirstTarget, default));
        Assert.NotNull(handle);
        Assert.True(handle.IsClosed);
    }

    [Fact]
    public async Task CancellationBeatsCompletedPartialResults()
    {
        using var cancellation = new CancellationTokenSource();
        var probes = new ProbeStub
        {
            Broadcast = _ =>
            {
                cancellation.Cancel();
                return Task.FromResult<IReadOnlyList<GreeDeviceInfo>>([Sample]);
            }
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new GreeDiscoveryService(probes).ScanAsync(cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task OldAddressFailureKeepsBroadcastResults()
    {
        var probes = new ProbeStub
        {
            Broadcast = _ => Task.FromResult<IReadOnlyList<GreeDeviceInfo>>([Sample]),
            Direct = (_, _, _) => throw NetworkDown()
        };
        var devices = await new GreeDiscoveryService(probes).ScanAsync("127.0.0.2");
        Assert.Equal(Sample, Assert.Single(devices));
    }

    [Fact]
    public async Task AdapterFailureIsReportedWhenNoDeviceWasFound()
    {
        var socketError = NetworkDown();
        var probes = new ProbeStub { Broadcast = _ => throw socketError };
        var error = await Assert.ThrowsAsync<IOException>(() => new GreeDiscoveryService(probes).ScanAsync());
        var failures = Assert.IsType<AggregateException>(error.InnerException);
        Assert.Same(socketError, Assert.Single(failures.InnerExceptions));
    }

    [Fact]
    public async Task DirectScanSurvivesAdapterEnumerationFailure()
    {
        var probes = new ProbeStub
        {
            Targets = () => throw new NetworkInformationException(),
            Direct = (ip, _, _) => Task.FromResult<GreeDeviceInfo?>(Sample with { IpAddress = ip })
        };
        var devices = await new GreeDiscoveryService(probes).ScanAsync("127.0.0.1");
        Assert.Equal(Sample, Assert.Single(devices));
    }

    [Fact]
    public async Task EmptySuccessfulScanRemainsAnEmptyList()
    {
        var devices = await new GreeDiscoveryService(new ProbeStub()).ScanAsync();
        Assert.Empty(devices);
    }

    [Fact]
    public async Task ParallelAdapterScansMergeByMac()
    {
        var second = IPAddress.Parse("127.0.0.2");
        var entered = 0;
        var bothEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probes = new ProbeStub
        {
            Targets = () => [FirstTarget, (second, second)],
            Broadcast = async token =>
            {
                if (Interlocked.Increment(ref entered) == 2) bothEntered.SetResult();
                await bothEntered.Task.WaitAsync(token);
                return [Sample, Sample];
            }
        };
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await new GreeDiscoveryService(probes).ScanAsync(cancellationToken: guard.Token);
        Assert.Equal(Sample, Assert.Single(result));
    }

    [Fact]
    public async Task TransientRefreshFailureStillTriesPreferredBinding()
    {
        var probes = new ProbeStub { Direct = (_, _, _) => throw NetworkDown() };
        var transport = new BindTransport(version => MakeReply(version, new { t = "bindok", key = "1234567890abcdef" }));
        var binding = new GreeBindingService(new GreeDiscoveryService(probes), transport);
        var session = await binding.BindAsync(Sample);

        Assert.Equal(Sample.EncryptionVersion, session.EncryptionVersion);
        Assert.Equal(1, transport.Calls);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("17")]
    [InlineData("\"001122334456\"")]
    public async Task PresentBindingIdentityMustMatch(string macJson)
    {
        using var mac = JsonDocument.Parse(macJson);
        var transport = new BindTransport(v =>
            MakeReply(v, new { t = "bindok", mac = mac.RootElement, key = "1234567890abcdef" }));
        var binding = new GreeBindingService(new GreeDiscoveryService(new ProbeStub()), transport);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => binding.BindAsync(Sample));
        var failures = Assert.IsType<AggregateException>(error.InnerException);
        Assert.Equal(2, failures.InnerExceptions.Count);
        Assert.All(failures.InnerExceptions, ex => Assert.Contains("outro aparelho", ex.Message));
    }

    [Fact]
    public async Task AlternativeVersionAcceptsCanonicalEquivalentMac()
    {
        var transport = new BindTransport(v => v == GreeEncryptionVersion.Ecb
            ? throw new TimeoutException()
            : MakeReply(v, new { t = "bindok", mac = "00-11-22-33-44-55", key = "1234567890abcdef" }));
        var service = new GreeBindingService(new GreeDiscoveryService(new ProbeStub()), transport);
        var session = await service.BindAsync(Sample with { Mac = "00:11:22:33:44:55" });

        Assert.Equal(2, transport.Calls);
        Assert.Equal(Mac, session.Mac);
        Assert.Equal(GreeEncryptionVersion.Gcm, session.EncryptionVersion);
    }

    [Fact]
    public async Task BindingStopsIfCancelledDuringRefresh()
    {
        using var cancel = new CancellationTokenSource();
        var probes = new ProbeStub { Direct = (_, _, _) => { cancel.Cancel(); return Task.FromResult<GreeDeviceInfo?>(null); } };
        var transport = new BindTransport(_ => throw new InvalidOperationException("Envio indevido"));
        var binding = new GreeBindingService(new GreeDiscoveryService(probes), transport);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => binding.BindAsync(Sample, cancel.Token));
        Assert.Equal(0, transport.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"pack\":null}")]
    [InlineData("{\"pack\":\"\",\"tag\":42}")]
    public void InvalidScanInputIsIgnored(string input)
    {
        Assert.Null(UdpScanProbes.TryParseScanResponse(Encoding.UTF8.GetBytes(input), new IPEndPoint(IPAddress.Loopback, 7000)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("teste")]
    public void InvalidMacIsRejected(string? mac)
    {
        Assert.False(GreeDiscoveryService.TryNormalizeMac(mac, out _));
    }

    [Theory]
    [InlineData(GreeEncryptionVersion.Ecb)]
    [InlineData(GreeEncryptionVersion.Gcm)]
    public void ValidScanAcceptsBothWireFormats(GreeEncryptionVersion version)
    {
        var wire = MakeReply(version, new { mac = "00:11:22:33:44:55", name = "Sala" });
        var device = UdpScanProbes.TryParseScanResponse(Encoding.UTF8.GetBytes(wire), new IPEndPoint(IPAddress.Loopback, 7000));
        Assert.NotNull(device);
        Assert.Equal((Mac, "Sala", version), (device.Mac, device.Name, device.EncryptionVersion));
    }

    [Fact]
    public void LegacyScanAcceptsExplicitNullTag()
    {
        var encoded = GreeCryptoService.EncryptGeneric(JsonSerializer.Serialize(new { mac = Mac }), GreeEncryptionVersion.Ecb);
        var wire = JsonSerializer.SerializeToUtf8Bytes(new { pack = encoded.Pack, tag = (string?)null });
        var device = UdpScanProbes.TryParseScanResponse(wire, new IPEndPoint(IPAddress.Loopback, 7000));
        Assert.Equal(GreeEncryptionVersion.Ecb, device?.EncryptionVersion);
    }

    [Fact]
    public void RepeatedScanRepliesRetainOneDevicePerMac()
    {
        var found = new Dictionary<string, GreeDeviceInfo>(StringComparer.OrdinalIgnoreCase);
        var endpoint = new IPEndPoint(IPAddress.Loopback, 7000);
        var wire = MakeReply(GreeEncryptionVersion.Ecb, new { mac = Mac, name = "Sala" });
        var packet = new UdpReceiveResult(Encoding.UTF8.GetBytes(wire), endpoint);
        for (var i = 0; i < 3; i++)
            UdpScanProbes.CollectScanResponse(found, packet);
        Assert.Equal(Mac, Assert.Single(found).Key);
    }

    private static string MakeReply(GreeEncryptionVersion version, object body)
    {
        var encoded = GreeCryptoService.EncryptGeneric(JsonSerializer.Serialize(body), version);
        return GreeBindingService.CreateEnvelope(Mac, encoded, version, 1);
    }

    private static SocketException NetworkDown() => new((int)SocketError.NetworkDown);

    private sealed class BindTransport(Func<GreeEncryptionVersion, string> reply) : IGreeTransport
    {
        public int Calls { get; private set; }

        public Task<string> SendAsync(string request, IPAddress ipAddress, int port, CancellationToken cancellationToken)
        {
            Calls++;
            using var command = JsonDocument.Parse(request);
            var version = command.RootElement.TryGetProperty("tag", out _) ? GreeEncryptionVersion.Gcm : GreeEncryptionVersion.Ecb;
            return Task.FromResult(reply(version));
        }
    }

    private sealed class ProbeStub : IGreeScanProbes
    {
        internal Func<IEnumerable<(IPAddress, IPAddress)>> Targets { get; init; } = () => [FirstTarget];
        internal Func<CancellationToken, Task<IReadOnlyList<GreeDeviceInfo>>> Broadcast { get; init; }
            = _ => Task.FromResult<IReadOnlyList<GreeDeviceInfo>>([]);
        internal Func<IPAddress, string?, CancellationToken, Task<GreeDeviceInfo?>> Direct { get; init; }
            = (_, _, _) => Task.FromResult<GreeDeviceInfo?>(null);

        public IEnumerable<(IPAddress LocalAddress, IPAddress BroadcastAddress)> GetBroadcastTargets() => Targets();
        public Task<IReadOnlyList<GreeDeviceInfo>> ScanBroadcastAsync(
            (IPAddress LocalAddress, IPAddress BroadcastAddress) target, CancellationToken cancellationToken) => Broadcast(cancellationToken);
        public Task<GreeDeviceInfo?> ScanAddressAsync(IPAddress address, string? requiredMac, CancellationToken cancellationToken)
            => Direct(address, requiredMac, cancellationToken);
    }
}
