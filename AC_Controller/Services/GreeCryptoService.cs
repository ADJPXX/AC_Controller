using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AC_Controller.Models;

namespace AC_Controller.Services;

public static class GreeCryptoService
{
    // Bootstrap keys used by the reverse-engineered Gree/EWPE local protocol.
    private const string GenericEcbKey = "a3K8Bx%2r8Y7#xDh";
    private const string GenericGcmKey = "{yxAHAY_Lm6pbC/<";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static GcmResult EncryptDevice(string plaintext, GreeSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Encrypt(plaintext, session.DeviceKey, session.EncryptionVersion);
    }

    public static string DecryptDevice(string pack, string? tag, GreeSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Decrypt(pack, tag, session.DeviceKey, session.EncryptionVersion);
    }

    public static GcmResult EncryptGeneric(string plaintext, GreeEncryptionVersion version)
    {
        return Encrypt(plaintext, GetGenericKey(version), version);
    }

    public static string DecryptGeneric(string pack, string? tag, GreeEncryptionVersion version)
    {
        return Decrypt(pack, tag, GetGenericKey(version), version);
    }

    private static GcmResult Encrypt(string plaintext, string keyText, GreeEncryptionVersion version)
    {
        ValidateVersion(version);
        var key = StrictUtf8.GetBytes(keyText);
        ValidateAesKey(key);
        var plaintextBytes = StrictUtf8.GetBytes(plaintext);

        if (version == GreeEncryptionVersion.Ecb)
        {
            using var aes = Aes.Create();
            aes.Key = key;
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.PKCS7;
            using var encryptor = aes.CreateEncryptor();
            var ciphertext = encryptor.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);
            return new GcmResult { Pack = Convert.ToBase64String(ciphertext) };
        }

        var gcmCiphertext = new byte[plaintextBytes.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(key, 16))
            aes.Encrypt(GreeResources.GcmIv, plaintextBytes, gcmCiphertext, tag, GreeResources.GcmAadBytes);

        return new GcmResult
        {
            Pack = Convert.ToBase64String(gcmCiphertext),
            Tag = Convert.ToBase64String(tag)
        };
    }

    private static string Decrypt(string pack, string? tag, string keyText, GreeEncryptionVersion version)
    {
        ValidateVersion(version);
        var key = StrictUtf8.GetBytes(keyText);
        ValidateAesKey(key);
        var ciphertext = Convert.FromBase64String(pack);

        if (version == GreeEncryptionVersion.Ecb)
        {
            using var aes = Aes.Create();
            aes.Key = key;
            aes.Mode = CipherMode.ECB;
            // Older firmware pads the JSON object with arbitrary bytes.
            aes.Padding = PaddingMode.None;
            using var decryptor = aes.CreateDecryptor();
            var plaintext = decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
            var reader = new Utf8JsonReader(plaintext);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                throw new CryptographicException("O pacote ECB descriptografado não contém um objeto JSON.");
            reader.Skip();
            return StrictUtf8.GetString(plaintext, 0, checked((int)reader.BytesConsumed));
        }

        if (string.IsNullOrWhiteSpace(tag))
            throw new CryptographicException("A resposta GCM não contém a tag de autenticação.");

        var tagBytes = Convert.FromBase64String(tag);
        var gcmPlaintext = new byte[ciphertext.Length];
        using (var aes = new AesGcm(key, 16))
            aes.Decrypt(GreeResources.GcmIv, ciphertext, tagBytes, gcmPlaintext, GreeResources.GcmAadBytes);

        var length = gcmPlaintext.Length;
        while (length > 0 && gcmPlaintext[length - 1] is 0xFF or 0x00)
            length--;

        return StrictUtf8.GetString(gcmPlaintext, 0, length);
    }

    private static string GetGenericKey(GreeEncryptionVersion version) => version switch
    {
        GreeEncryptionVersion.Ecb => GenericEcbKey,
        GreeEncryptionVersion.Gcm => GenericGcmKey,
        _ => throw new ArgumentOutOfRangeException(nameof(version))
    };

    private static void ValidateAesKey(byte[] key)
    {
        if (key.Length is not (16 or 24 or 32))
            throw new CryptographicException("A chave recebida não tem um tamanho válido para AES.");
    }

    private static void ValidateVersion(GreeEncryptionVersion version)
    {
        if (version is not (GreeEncryptionVersion.Ecb or GreeEncryptionVersion.Gcm))
            throw new ArgumentOutOfRangeException(nameof(version));
    }
}
