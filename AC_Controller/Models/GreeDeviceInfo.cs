using System.Net;

namespace AC_Controller.Models;

public enum GreeEncryptionVersion
{
    Ecb = 1,
    Gcm = 2
}

public sealed record GreeDeviceInfo(
    string Mac,
    IPAddress IpAddress,
    int Port,
    GreeEncryptionVersion EncryptionVersion,
    string Name,
    string Model)
{
    public string DisplayName =>
        $"{(string.IsNullOrWhiteSpace(Name) ? "Gree" : Name)}  |  " +
        $"{(string.IsNullOrWhiteSpace(Model) ? "Modelo desconhecido" : Model)}  |  " +
        $"{Mac}  |  {IpAddress}";
}

public sealed record GreeSession(
    string Mac,
    IPAddress IpAddress,
    int Port,
    GreeEncryptionVersion EncryptionVersion,
    string DeviceKey);
