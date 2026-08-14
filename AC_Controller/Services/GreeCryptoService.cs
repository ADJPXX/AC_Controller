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
            throw new InvalidOperationException("Device Key não configurada.");

        _deviceKey = GreeResources.DeviceKey;
    }

    public GcmResult Encrypt(string plaintext)
    {
        var key = Encoding.UTF8.GetBytes(_deviceKey);

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);

        var ciphertext = new byte[plaintextBytes.Length];

        var tag = new byte[16];

        var aad = Encoding.UTF8.GetBytes(GreeResources.GcmAad);

        using var aes = new AesGcm(key, 16);

        aes.Encrypt(GreeResources.GcmIv, plaintextBytes, ciphertext, tag, aad);

        return new GcmResult
        {
            Pack = Convert.ToBase64String(ciphertext),
            Tag = Convert.ToBase64String(tag)
        };
    }

    public string Decrypt(string pack, string tag)
    {
        var key = Encoding.UTF8.GetBytes(_deviceKey);

        var ciphertext = Convert.FromBase64String(pack);

        var tagBytes = Convert.FromBase64String(tag);

        var plaintext = new byte[ciphertext.Length];

        var aad = Encoding.UTF8.GetBytes(GreeResources.GcmAad);

        using var aes = new AesGcm(key, 16);

        aes.Decrypt(GreeResources.GcmIv, ciphertext, tagBytes, plaintext, aad);

        return Encoding.UTF8.GetString(plaintext).Replace("\u00FF", "");
    }
}