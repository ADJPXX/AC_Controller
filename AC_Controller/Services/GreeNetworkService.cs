using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AC_Controller.Services;

public sealed class GreeNetworkService
{
    private readonly IPAddress _ip;

    public GreeNetworkService()
    {
        if (!IPAddress.TryParse(
                GreeResources.Ip,
                out IPAddress? ip))
        {
            throw new InvalidOperationException(
                $"IP inválido: {GreeResources.Ip}");
        }

        _ip = ip;
    }

    public async Task<string> SendAsync(
        string request)
    {
        using var client =
            new UdpClient();

        byte[] data =
            Encoding.UTF8.GetBytes(request);

        var endpoint =
            new IPEndPoint(
                _ip,
                GreeResources.Port);

        await client.SendAsync(
            data,
            endpoint);

        try
        {
            UdpReceiveResult response =
                await client.ReceiveAsync()
                    .WaitAsync(
                        TimeSpan.FromSeconds(
                            GreeResources.ReceiveTimeoutSeconds));

            return Encoding.UTF8.GetString(
                response.Buffer);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException(
                $"O Gree não respondeu dentro de " +
                $"{GreeResources.ReceiveTimeoutSeconds} segundos.");
        }
    }
}