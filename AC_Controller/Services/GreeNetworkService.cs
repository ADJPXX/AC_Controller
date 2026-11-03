using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AC_Controller.Services;

public sealed class GreeNetworkService
{
    public async Task<string> SendAsync(
        string request,
        IPAddress ipAddress,
        int port,
        CancellationToken cancellationToken = default)
    {
        using var client = new UdpClient(AddressFamily.InterNetwork);
        var data = Encoding.UTF8.GetBytes(request);
        var endpoint = new IPEndPoint(ipAddress, port);

        await client.SendAsync(data, data.Length, endpoint);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(GreeResources.ReceiveTimeoutSeconds));

        while (true)
        {
            UdpReceiveResult response;
            try
            {
                response = await client.ReceiveAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"O aparelho Gree não respondeu em {GreeResources.ReceiveTimeoutSeconds} segundos.");
            }

            if (response.RemoteEndPoint.Address.Equals(ipAddress) && response.RemoteEndPoint.Port == port)
                return Encoding.UTF8.GetString(response.Buffer);
        }
    }
}
