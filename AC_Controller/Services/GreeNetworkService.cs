using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AC_Controller.Services;

internal interface IGreeTransport
{
    Task<string> SendAsync(string request, IPAddress ipAddress, int port, CancellationToken cancellationToken = default);
}

public sealed class GreeNetworkService : IGreeTransport
{
    private static readonly Encoding WireEncoding = new UTF8Encoding(false, true);
    private readonly TimeSpan _timeout;

    public GreeNetworkService() : this(TimeSpan.FromSeconds(GreeResources.ReceiveTimeoutSeconds)) { }

    internal GreeNetworkService(TimeSpan timeout) => _timeout = timeout;

    public async Task<string> SendAsync(
        string request,
        IPAddress ipAddress,
        int port,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);
        using var client = new UdpClient(AddressFamily.InterNetwork);
        var endpoint = new IPEndPoint(ipAddress, port);
        var data = WireEncoding.GetBytes(request);
        try
        {
            await client.SendAsync(data, endpoint, deadline.Token).ConfigureAwait(false);
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                var response = await client.ReceiveAsync(deadline.Token).ConfigureAwait(false);
                if (response.RemoteEndPoint.Equals(endpoint))
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    return WireEncoding.GetString(response.Buffer);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (OperationCanceledException ex) when (deadline.IsCancellationRequested)
        {
            throw new TimeoutException($"O aparelho Gree não respondeu em {_timeout.TotalSeconds:g} segundos.", ex);
        }
    }
}
