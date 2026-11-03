using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using AC_Controller.Models;

namespace AC_Controller.Services;

public sealed class GreeBindingService(GreeDiscoveryService discovery, GreeNetworkService network)
{
    public async Task<GreeSession> BindAsync(GreeDeviceInfo device, CancellationToken cancellationToken = default)
    {
        var candidates = new[]
        {
            device.EncryptionVersion,
            device.EncryptionVersion == GreeEncryptionVersion.Ecb
                ? GreeEncryptionVersion.Gcm
                : GreeEncryptionVersion.Ecb
        };

        Exception? lastError = null;

        foreach (var version in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Some modules only accept bind shortly after scan. Refresh immediately before each attempt.
            var refreshed = await discovery.RefreshDeviceAsync(device.Mac, device.IpAddress, cancellationToken);
            var target = refreshed ?? device;

            try
            {
                var key = await BindOnceAsync(target, version, cancellationToken);
                return new GreeSession(target.Mac, target.IpAddress, target.Port, version, key);
            }
            catch (Exception ex) when (ex is TimeoutException or CryptographicException or JsonException or
                                       FormatException or InvalidOperationException or ArgumentException or SocketException)
            {
                lastError = ex;
            }
        }

        throw new InvalidOperationException(
            $"Não foi possível vincular ao aparelho Gree {device.Mac}. Verifique se ele está acessível na mesma rede Wi-Fi.",
            lastError);
    }

    private async Task<string> BindOnceAsync(
        GreeDeviceInfo device,
        GreeEncryptionVersion version,
        CancellationToken cancellationToken)
    {
        var bindData = version == GreeEncryptionVersion.Gcm
            ? JsonSerializer.Serialize(new { cid = device.Mac, mac = device.Mac, t = "bind", uid = 0 })
            : JsonSerializer.Serialize(new { mac = device.Mac, t = "bind", uid = 0 });

        var encrypted = GreeCryptoService.EncryptGeneric(bindData, version);
        var request = CreateEnvelope(device.Mac, encrypted, version, id: 1);
        var response = await network.SendAsync(request, device.IpAddress, device.Port, cancellationToken);

        using var outerDocument = JsonDocument.Parse(response);
        var outer = outerDocument.RootElement;
        if (!outer.TryGetProperty("pack", out var packElement) || packElement.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("A resposta de vínculo não contém 'pack'.");

        var pack = packElement.GetString();
        if (string.IsNullOrWhiteSpace(pack))
            throw new InvalidOperationException("A resposta de vínculo contém um pacote vazio.");

        string? tag = null;
        if (outer.TryGetProperty("tag", out var tagElement) && tagElement.ValueKind == JsonValueKind.String)
            tag = tagElement.GetString();

        var plaintext = GreeCryptoService.DecryptGeneric(pack, tag, version);
        using var innerDocument = JsonDocument.Parse(plaintext);
        var inner = innerDocument.RootElement;

        if (!inner.TryGetProperty("t", out var typeElement) ||
            !string.Equals(typeElement.GetString(), "bindok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("O aparelho não confirmou o vínculo ('bindok').");

        if (inner.TryGetProperty("mac", out var macElement) &&
            macElement.ValueKind == JsonValueKind.String &&
            !string.Equals(GreeDiscoveryService.NormalizeMac(macElement.GetString() ?? string.Empty), device.Mac,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A resposta de vínculo pertence a outro aparelho.");

        if (inner.TryGetProperty("r", out var resultElement) &&
            (resultElement.ValueKind != JsonValueKind.Number || !resultElement.TryGetInt32(out var result) || result != 200))
            throw new InvalidOperationException("O aparelho recusou o vínculo.");

        if (!inner.TryGetProperty("key", out var keyElement) || keyElement.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("A resposta de vínculo não contém a chave do aparelho.");

        var key = keyElement.GetString();
        if (string.IsNullOrWhiteSpace(key) || EncodingLength(key) is not (16 or 24 or 32))
            throw new InvalidOperationException("O aparelho devolveu uma chave AES inválida.");

        return key;
    }

    private static int EncodingLength(string value) => System.Text.Encoding.UTF8.GetByteCount(value);

    internal static string CreateEnvelope(string mac, GcmResult encrypted, GreeEncryptionVersion version, int id)
    {
        var envelope = new Dictionary<string, object?>
        {
            ["cid"] = "app",
            ["i"] = id,
            ["t"] = "pack",
            ["uid"] = 0,
            ["tcid"] = mac,
            ["pack"] = encrypted.Pack
        };

        if (version == GreeEncryptionVersion.Gcm)
            envelope["tag"] = encrypted.Tag;

        return JsonSerializer.Serialize(envelope);
    }
}
