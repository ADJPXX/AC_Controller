using System.Security.Cryptography;
using System.Text;
using AC_Controller.Models;

namespace AC_Controller.Services;

public sealed class GreeCryptoService
{
    private readonly string _deviceKey;

    public GreeCryptoService()
    {
        if (string.IsNullOrWhiteSpace(GreeResources.DeviceKey))
        {
            throw new InvalidOperationException(
                "Device Key não configurada.");
        }

        _deviceKey = GreeResources.DeviceKey;
    }

    public GcmResult Encrypt(string plaintext)
    {
        byte[] key =
            Encoding.UTF8.GetBytes(_deviceKey);

        byte[] plaintextBytes =
            Encoding.UTF8.GetBytes(plaintext);

        byte[] ciphertext =
            new byte[plaintextBytes.Length];

        byte[] tag =
            new byte[16];

        byte[] aad =
            Encoding.UTF8.GetBytes(
                GreeResources.GcmAad);

        using var aes =
            new AesGcm(key, 16);

        aes.Encrypt(
            GreeResources.GcmIv,
            plaintextBytes,
            ciphertext,
            tag,
            aad);

        return new GcmResult
        {
            Pack = Convert.ToBase64String(ciphertext),
            Tag = Convert.ToBase64String(tag)
        };
    }

    public string Decrypt(
        string pack,
        string tag)
    {
        byte[] key =
            Encoding.UTF8.GetBytes(_deviceKey);

        byte[] ciphertext =
            Convert.FromBase64String(pack);

        byte[] tagBytes =
            Convert.FromBase64String(tag);

        byte[] plaintext =
            new byte[ciphertext.Length];

        byte[] aad =
            Encoding.UTF8.GetBytes(
                GreeResources.GcmAad);

        using var aes =
            new AesGcm(key, 16);

        aes.Decrypt(
            GreeResources.GcmIv,
            ciphertext,
            tagBytes,
            plaintext,
            aad);

        return Encoding.UTF8.GetString(
                plaintext)
            .Replace("\u00FF", "");
    }
}