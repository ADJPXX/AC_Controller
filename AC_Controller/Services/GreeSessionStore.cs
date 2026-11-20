using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AC_Controller.Models;

namespace AC_Controller.Services;

public sealed class GreeSessionStore
{
    // The protected record contains one IPv4 endpoint, one MAC, the algorithm and one AES key.
    private const int MaxSessionBytes = 1024 * 1024;
    private readonly string _directory;
    private string SessionPath => Path.Combine(_directory, "session.dat");

    public GreeSessionStore() : this(ConfigService.DataDirectory) { }

    internal GreeSessionStore(string directory) => _directory = directory;

    public async Task<GreeSession?> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[] protectedBytes;
        try
        {
            protectedBytes = await Task.Run(() => AtomicFile.Read(SessionPath, MaxSessionBytes, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (InvalidDataException) { return null; }

        byte[]? plaintext = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (protectedBytes.Length == 0)
                return null;
            plaintext = WindowsDataProtection.Unprotect(protectedBytes);
            var saved = JsonSerializer.Deserialize<PersistedSession>(plaintext);
            cancellationToken.ThrowIfCancellationRequested();
            if (saved is null || !IPAddress.TryParse(saved.IpAddress, out var ip))
                return null;
            return ValidatedSession(saved.Mac, ip, saved.Port, (GreeEncryptionVersion)saved.EncryptionVersion, saved.DeviceKey);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(session);
        var validated = ValidatedSession(session.Mac, session.IpAddress, session.Port, session.EncryptionVersion, session.DeviceKey)
                        ?? throw new ArgumentException("A sessão Gree é inválida.", nameof(session));
        Directory.CreateDirectory(_directory);
        var saved = new PersistedSession(validated.Mac, validated.IpAddress.ToString(), validated.Port,
            (int)validated.EncryptionVersion, validated.DeviceKey);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(saved);
        byte[]? protectedBytes = null;
        try
        {
            protectedBytes = WindowsDataProtection.Protect(plaintext);
            await AtomicFile.WriteAsync(SessionPath, protectedBytes, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            if (protectedBytes is not null)
                CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }

    private static GreeSession? ValidatedSession(string? mac, IPAddress? ip, int port, GreeEncryptionVersion version, string? key)
    {
        if (!GreeDiscoveryService.TryNormalizeMac(mac, out var normalized) ||
            ip?.AddressFamily != AddressFamily.InterNetwork || port is < 1 or > 65535 ||
            !Enum.IsDefined(version) || string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) is not (16 or 24 or 32))
            return null;
        return new GreeSession(normalized, ip, port, version, key);
    }

    private sealed record PersistedSession(string? Mac, string? IpAddress, int Port, int EncryptionVersion, string? DeviceKey);
}
