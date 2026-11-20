using System.Net;
using System.Security.Cryptography;
using System.Text;
using AC_Controller.Models;
using AC_Controller.Services;

namespace AC_Controller.Tests;

public sealed class CryptoAndResourcesTests
{
    private static GreeSession Session(GreeEncryptionVersion version) =>
        new("001122334455", IPAddress.Loopback, 7000, version, "test-only-key-16");

    [Theory]
    [InlineData(GreeEncryptionVersion.Ecb)]
    [InlineData(GreeEncryptionVersion.Gcm)]
    public void SupportedVersionsRoundTripUnicode(GreeEncryptionVersion version)
    {
        const string json = """{"name":"Salão","nested":{"name":"}"}}""";
        var session = Session(version);
        var result = GreeCryptoService.EncryptDevice(json, session);
        Assert.Equal(json, GreeCryptoService.DecryptDevice(result.Pack, result.Tag, session));
    }

    [Fact]
    public void LegacyPaddingMayContainClosingBrace()
    {
        const string json = """{"name":"}"}""";
        var input = Enumerable.Repeat((byte)'}', 16).ToArray();
        Encoding.UTF8.GetBytes(json).CopyTo(input, 0);
        var session = Session(GreeEncryptionVersion.Ecb);
        using var aes = Aes.Create();
        aes.Key = Encoding.UTF8.GetBytes(session.DeviceKey);
        var pack = Convert.ToBase64String(aes.EncryptEcb(input, PaddingMode.None));
        Assert.Equal(json, GreeCryptoService.DecryptDevice(pack, null, session));
    }

    [Theory]
    [InlineData(GreeEncryptionVersion.Ecb)]
    [InlineData(GreeEncryptionVersion.Gcm)]
    public void InvalidUtf8InTextIsRejected(GreeEncryptionVersion version)
    {
        var session = Session(version);
        byte[] data = [.. "{\"name\":\""u8, 0xff, .. "\"}"u8];
        var key = Encoding.UTF8.GetBytes(session.DeviceKey);
        byte[] bytes;
        string? tag = null;
        if (version == GreeEncryptionVersion.Ecb)
        {
            using var aes = Aes.Create();
            aes.Key = key;
            bytes = aes.EncryptEcb(data, PaddingMode.PKCS7);
        }
        else
        {
            using var gcm = new AesGcm(key, 16);
            bytes = new byte[data.Length];
            var tagBytes = new byte[16];
            gcm.Encrypt(GreeResources.GcmIv, data, bytes, tagBytes, GreeResources.GcmAadBytes);
            tag = Convert.ToBase64String(tagBytes);
        }
        Assert.Throws<DecoderFallbackException>(() =>
            GreeCryptoService.DecryptDevice(Convert.ToBase64String(bytes), tag, session));
    }

    [Fact]
    public void UnknownSessionVersionCannotFallThroughToGcm()
    {
        var invalid = Session((GreeEncryptionVersion)17);
        Assert.Throws<ArgumentOutOfRangeException>(() => GreeCryptoService.EncryptDevice("{}", invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => GreeCryptoService.DecryptDevice("", null, invalid));
    }

    [Fact]
    public void SessionFormattingOmitsSecretKey()
    {
        var session = Session(GreeEncryptionVersion.Gcm);
        Assert.Contains(session.Mac, session.ToString());
        Assert.DoesNotContain(session.DeviceKey, session.ToString());
    }

    [Fact]
    public void ProtocolConstantsSurviveConcurrentConsumerMutation()
    {
        var iv = GreeResources.GcmIv.ToArray();
        var aad = GreeResources.GcmAadBytes.ToArray();
        var columns = GreeResources.StatusColumns.ToArray();
        Parallel.For(0, 128, i =>
        {
            GreeResources.GcmIv[0] = (byte)i;
            GreeResources.GcmAadBytes[0] = (byte)i;
            GreeResources.StatusColumns[0] = i.ToString();
        });
        Assert.Equal(iv, GreeResources.GcmIv);
        Assert.Equal(aad, GreeResources.GcmAadBytes);
        Assert.Equal(columns, GreeResources.StatusColumns);
    }

    [Fact]
    public void MissingTagAndBadKeyRemainErrors()
    {
        var session = Session(GreeEncryptionVersion.Gcm);
        Assert.Throws<CryptographicException>(() => GreeCryptoService.DecryptDevice("YQ==", null, session));
        Assert.Throws<CryptographicException>(() => GreeCryptoService.EncryptDevice("{}", session with { DeviceKey = "" }));
        Assert.Throws<ArgumentNullException>(() => GreeCryptoService.EncryptDevice("{}", null!));
    }
}
