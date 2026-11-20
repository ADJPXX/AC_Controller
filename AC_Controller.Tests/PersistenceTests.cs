using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AC_Controller.Models;
using AC_Controller.Services;

namespace AC_Controller.Tests;

public sealed class PersistenceTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("ACController-storage-");
    private string Settings => Path.Combine(_directory.FullName, "settings.json");
    private string Legacy => Path.Combine(_directory.FullName, "AC_Controller.json");
    private string SessionFile => Path.Combine(_directory.FullName, "session.dat");
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(20);

    private static Config Preferences(int index) => new()
    {
        DeviceMac = index.ToString("x12"), LastKnownIp = $"192.0.2.{index}", StartWithWindows = index % 2 == 0
    };

    private Config Read(Action<Exception>? warn = null) =>
        ConfigService.ReadJson(_directory.FullName, Legacy, warn ?? (ex => throw ex));

    private void Save(Config config) => ConfigService.Save(_directory.FullName, config);

    private GreeSessionStore Store() => new(_directory.FullName);

    private static GreeSession Session(int index) => new(index.ToString("x12"), IPAddress.Loopback, 7000 + index,
        GreeEncryptionVersion.Gcm, index.ToString("D16"));

    [Fact]
    public async Task ParallelSettingsWritersPublishOnlyWholeSnapshots()
    {
        Save(Preferences(0));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workers = Enumerable.Range(1, 32).Select(async index =>
        {
            await start.Task.ConfigureAwait(false);
            Save(Preferences(index));
            var read = Read();
            var version = Convert.ToInt32(read.DeviceMac!, 16);
            Assert.InRange(version, 0, 32);
            Assert.Equal(Preferences(version).LastKnownIp, read.LastKnownIp);
            Assert.Equal(Preferences(version).StartWithWindows, read.StartWithWindows);
        }).ToArray();
        start.SetResult();
        await Task.WhenAll(workers).WaitAsync(Deadline);
        Assert.Empty(_directory.GetFiles("*.tmp"));
    }

    [Fact]
    public void ConcurrentInitializationCannotOverwriteAnExistingSelection()
    {
        Save(Preferences(4));
        Assert.False(AtomicFile.Write(Settings, "{}"u8.ToArray(), overwrite: false));
        Assert.Equal(Preferences(4).DeviceMac, Read().DeviceMac);
        Assert.Empty(_directory.GetFiles("*.tmp"));
    }

    [Fact]
    public async Task BusyDestinationLeavesPriorSettingsAndSessionAndCleansTemporaryFiles()
    {
        Save(Preferences(1));
        var store = Store();
        await store.SaveAsync(Session(1));
        using (File.Open(Settings, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.True(Record.Exception(() => Save(Preferences(2))) is IOException or UnauthorizedAccessException);
        using (File.Open(SessionFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.True(await Record.ExceptionAsync(() => store.SaveAsync(Session(2))) is IOException or UnauthorizedAccessException);
        Assert.Equal(Preferences(1).DeviceMac, Read().DeviceMac);
        Assert.Equal(Session(1), await store.LoadAsync());
        Assert.Empty(_directory.GetFiles("*.tmp"));
    }

    [Fact]
    public void InterruptedLegacyScrubIsRetriedAndCurrentPreferencesWin()
    {
        File.WriteAllText(Legacy, """{"DeviceMac":"000000000002","StartWithWindows":true,"OldKey":"fixture"}""");
        var warnings = new List<Exception>();
        using (File.Open(Legacy, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var migrated = Read(warnings.Add);
            Assert.True(migrated.StartWithWindows);
            Assert.Equal("000000000002", migrated.DeviceMac);
            Assert.Single(warnings);
        }
        Assert.DoesNotContain("OldKey", File.ReadAllText(Settings));
        Save(Preferences(3));
        Assert.Equal(Preferences(3).DeviceMac, Read().DeviceMac);
        using var safeLegacy = JsonDocument.Parse(File.ReadAllText(Legacy));
        Assert.False(safeLegacy.RootElement.TryGetProperty("OldKey", out _));
        Assert.Equal("000000000002", safeLegacy.RootElement.GetProperty("DeviceMac").GetString());
        Assert.Empty(_directory.GetFiles("*.tmp"));
    }

    [Fact]
    public void InvalidConfiguredMacAllowsNewSelectionAndPreservesOtherOptions()
    {
        Save(new Config { DeviceMac = "invalid", StartWithWindows = true, LastKnownIp = "192.0.2.5" });
        var warnings = new List<Exception>();
        var config = Read(warnings.Add);
        Assert.Null(config.DeviceMac);
        Assert.True(config.StartWithWindows);
        Assert.Equal("192.0.2.5", config.LastKnownIp);
        Assert.Single(warnings);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("""{"StartWithWindows":"true"}""")]
    public void MalformedSettingsAreReportedWithoutOverwritingTheOriginal(string text)
    {
        File.WriteAllText(Settings, text);
        Assert.ThrowsAny<JsonException>(() => Read());
        Assert.Equal(text, File.ReadAllText(Settings));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidUtf8IsReportedWithoutChangingTheFile(bool withByteOrderMark)
    {
        var bytes = new List<byte>();
        if (withByteOrderMark) bytes.AddRange(Encoding.UTF8.GetPreamble());
        bytes.AddRange("{\"DeviceMac\":\""u8.ToArray());
        bytes.Add(0xff);
        bytes.AddRange("\"}"u8.ToArray());
        File.WriteAllBytes(Settings, bytes.ToArray());
        Assert.Throws<InvalidDataException>(() => Read());
        Assert.Equal(bytes.ToArray(), File.ReadAllBytes(Settings));
    }

    [Fact]
    public async Task ParallelSessionSavesAndLoadsProduceCompleteDecryptableRecords()
    {
        await Store().SaveAsync(Session(0));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workers = Enumerable.Range(1, 24).Select(async index =>
        {
            var store = Store();
            await start.Task.ConfigureAwait(false);
            await store.SaveAsync(Session(index));
            var read = Assert.IsType<GreeSession>(await store.LoadAsync());
            Assert.Equal(Session(read.Port - 7000), read);
        }).ToArray();
        start.SetResult();
        await Task.WhenAll(workers).WaitAsync(Deadline);
        Assert.Empty(_directory.GetFiles("*.tmp"));
    }

    [Fact]
    public async Task CancellationTakesPrecedenceOverMissingCacheAndPreventsPublication()
    {
        var store = Store();
        var canceled = new CancellationToken(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.LoadAsync(canceled));
        await store.SaveAsync(Session(1));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(Session(2), canceled));
        Assert.Equal(Session(1), await store.LoadAsync());
        Assert.Empty(_directory.GetFiles("*.tmp"));
    }

    [Theory]
    [InlineData("gggggggggggg", "127.0.0.1", 7000, 1, "0123456789abcdef")]
    [InlineData("400000000001", "::1", 7000, 1, "0123456789abcdef")]
    [InlineData("400000000001", "127.0.0.1", 7000, 1, "x")]
    [InlineData("400000000001", "127.0.0.1", 0, 1, "0123456789abcdef")]
    [InlineData("400000000001", "127.0.0.1", 7000, 5, "0123456789abcdef")]
    public async Task SemanticallyInvalidCacheCannotBeLoadedOrPublished(string mac, string ip, int port, int version, string key)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new { Mac = mac, IpAddress = ip, Port = port, EncryptionVersion = version, DeviceKey = key });
        File.WriteAllBytes(SessionFile, WindowsDataProtection.Protect(json));
        var store = Store();
        Assert.Null(await store.LoadAsync());
        var invalid = new GreeSession(mac, IPAddress.Parse(ip), port, (GreeEncryptionVersion)version, key);
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(invalid));
    }

    [Fact]
    public async Task NullSaveAndUnreadableOrOversizedCacheDoNotReplacePreferences()
    {
        var store = Store();
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.SaveAsync(null!));
        Assert.Throws<ArgumentNullException>(() => Save(null!));
        using (var oversized = File.Create(SessionFile))
            oversized.SetLength(1024 * 1024 + 1);
        Assert.Null(await store.LoadAsync());
        File.WriteAllBytes(SessionFile, "corrupt"u8.ToArray());
        Assert.Null(await store.LoadAsync());
        File.WriteAllBytes(SessionFile, WindowsDataProtection.Protect("{"u8.ToArray()));
        Assert.Null(await store.LoadAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OversizedPreferencesOrLegacyAreRejectedBeforeDecoding(bool legacy)
    {
        var path = legacy ? Legacy : Settings;
        using (var storage = File.Create(path))
            storage.SetLength(1024 * 1024 + 1);
        Assert.Throws<InvalidDataException>(() => Read());
        Assert.Equal(1024 * 1024 + 1, new FileInfo(path).Length);
        if (legacy)
            Assert.False(File.Exists(Settings));
    }

    [Fact]
    public void OversizedSavePreservesThePreviousReadableSettings()
    {
        Save(Preferences(1));
        Assert.Throws<InvalidDataException>(() => Save(new Config { LastKnownIp = new string('a', 1024 * 1024) }));
        Assert.Equal(Preferences(1).DeviceMac, Read().DeviceMac);
    }

    [Fact]
    public void DpapiMaintainsCompatibilityAndDelegatesNativeLifetimeToTheFramework()
    {
        var entropy = "AC_Controller.Session.v1"u8.ToArray();
        var clear = "session fixture"u8.ToArray();
        var legacy = ProtectedData.Protect(clear, entropy, DataProtectionScope.CurrentUser);
        Assert.Equal(clear, WindowsDataProtection.Unprotect(legacy));
        Assert.Equal(clear, ProtectedData.Unprotect(WindowsDataProtection.Protect(clear), entropy, DataProtectionScope.CurrentUser));
        Assert.ThrowsAny<CryptographicException>(() => WindowsDataProtection.Unprotect("invalid"u8.ToArray()));
        var methods = typeof(WindowsDataProtection).GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        Assert.DoesNotContain(methods, method => method.IsDefined(typeof(DllImportAttribute)));
    }

    [Fact]
    public void StartupRegistrationRequestsWriteOnlyWhenTheStoredValueMustChange()
    {
        var changes = new List<string>();
        void Verify(bool enabled, string? exe, string? current) =>
            StartupService.UpdateRegistration(enabled, exe, current, changes.Add, () => changes.Add("delete"));
        const string path = @"C:\Fixture Path\App.exe";
        Verify(false, null, null);
        Verify(true, path, $"\"{path}\"");
        Assert.Empty(changes);
        Assert.Throws<InvalidOperationException>(() => Verify(true, null, null));
        Verify(true, path, null);
        Verify(false, null, "old");
        Assert.Equal(new[] { $"\"{path}\"", "delete" }, changes);
    }

    public void Dispose() => _directory.Delete(recursive: true);
}
