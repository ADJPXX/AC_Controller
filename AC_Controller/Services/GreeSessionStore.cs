using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using AC_Controller.Models;

namespace AC_Controller.Services;

public sealed class GreeSessionStore
{
    private static readonly string SessionPath = Path.Combine(ConfigService.DataDirectory, "session.dat");

    public async Task<GreeSession?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SessionPath))
            return null;

        var protectedBytes = await File.ReadAllBytesAsync(SessionPath, cancellationToken);
        byte[]? plaintext = null;
        try
        {
            plaintext = WindowsDataProtection.Unprotect(protectedBytes);
            var saved = JsonSerializer.Deserialize<PersistedSession>(plaintext);
            if (saved is null || !IPAddress.TryParse(saved.IpAddress, out var ip) ||
                saved.Port is < 1 or > 65535 || string.IsNullOrWhiteSpace(saved.Mac) ||
                string.IsNullOrWhiteSpace(saved.DeviceKey) ||
                !Enum.IsDefined(typeof(GreeEncryptionVersion), saved.EncryptionVersion))
                return null;

            return new GreeSession(
                GreeDiscoveryService.NormalizeMac(saved.Mac),
                ip,
                saved.Port,
                (GreeEncryptionVersion)saved.EncryptionVersion,
                saved.DeviceKey);
        }
        catch (Exception ex) when (ex is CryptographicException or System.ComponentModel.Win32Exception or JsonException)
        {
            return null;
        }
        finally
        {
            if (plaintext is not null)
                CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }

    public async Task SaveAsync(GreeSession session, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(ConfigService.DataDirectory);
        var saved = new PersistedSession(
            session.Mac,
            session.IpAddress.ToString(),
            session.Port,
            (int)session.EncryptionVersion,
            session.DeviceKey);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(saved);
        byte[]? protectedBytes = null;

        try
        {
            protectedBytes = WindowsDataProtection.Protect(plaintext);
            var tempPath = SessionPath + ".tmp";
            await File.WriteAllBytesAsync(tempPath, protectedBytes, cancellationToken);
            File.Move(tempPath, SessionPath, true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            if (protectedBytes is not null)
                CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }

    private sealed record PersistedSession(
        string Mac,
        string IpAddress,
        int Port,
        int EncryptionVersion,
        string DeviceKey);
}
