using System.Net;
using System.Net.Sockets;
using System.Text;
using AC_Controller.Services;
using Xunit;

namespace AC_Controller.Tests;

public sealed class GreeNetworkTests
{
    private static readonly TimeSpan TestLimit = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task PrecancelledSendDoesNotWriteDatagram()
    {
        using var peer = CreateListener(out var port);
        var token = new CancellationToken(canceled: true);
        var network = new GreeNetworkService();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => network.SendAsync("unidade", IPAddress.Loopback, port, token));

        Assert.Equal(token, error.CancellationToken);
        Assert.False(peer.Client.Poll(50_000, SelectMode.SelectRead));
    }

    [Fact]
    public async Task CancellationDuringReceivePreservesCallerToken()
    {
        using var peer = CreateListener(out var port);
        using var cancel = new CancellationTokenSource();
        var request = new GreeNetworkService().SendAsync("unidade", IPAddress.Loopback, port, cancel.Token);
        await peer.ReceiveAsync().WaitAsync(TestLimit);
        cancel.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request.WaitAsync(TestLimit));
        Assert.Equal(cancel.Token, error.CancellationToken);
    }

    [Fact]
    public async Task UnansweredExchangeRaisesTimeout()
    {
        using var listener = CreateListener(out var port);
        var network = new GreeNetworkService(TimeSpan.FromMilliseconds(125));
        var request = network.SendAsync("unidade", IPAddress.Loopback, port);

        var timeout = await Assert.ThrowsAsync<TimeoutException>(() => request.WaitAsync(TestLimit));
        Assert.IsAssignableFrom<OperationCanceledException>(timeout.InnerException);
        Assert.True(listener.Client.Poll(50_000, SelectMode.SelectRead));
    }

    [Fact]
    public async Task InvalidUtf8IsRejectedAtTransportBoundary()
    {
        using var peer = CreateListener(out var port);
        var request = new GreeNetworkService().SendAsync("unidade", IPAddress.Loopback, port);
        var envelope = await peer.ReceiveAsync().WaitAsync(TestLimit);
        await peer.SendAsync(new byte[] { 0xFF }, envelope.RemoteEndPoint);
        await Assert.ThrowsAsync<DecoderFallbackException>(() => request.WaitAsync(TestLimit));
    }

    [Fact]
    public async Task ConcurrentExchangesReceiveTheirOwnOutOfOrderReplies()
    {
        const int count = 32;
        using var peer = CreateListener(out var port);
        using var guard = new CancellationTokenSource(TestLimit);
        var network = new GreeNetworkService(TestLimit);
        var messages = Enumerable.Range(0, count).Select(i => $"eco-{i}").ToArray();
        var requests = messages.Select(s => network.SendAsync(s, IPAddress.Loopback, port, guard.Token)).ToArray();
        var arrivals = new List<UdpReceiveResult>();
        for (var i = 0; i < count; i++)
            arrivals.Add(await peer.ReceiveAsync(guard.Token));
        arrivals.Reverse();
        foreach (var arrival in arrivals)
            await peer.SendAsync(arrival.Buffer, arrival.RemoteEndPoint, guard.Token);
        Assert.Equal(messages, await Task.WhenAll(requests).WaitAsync(guard.Token));
    }

    private static UdpClient CreateListener(out int port)
    {
        var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        port = ((IPEndPoint)listener.Client.LocalEndPoint!).Port;
        return listener;
    }
}
