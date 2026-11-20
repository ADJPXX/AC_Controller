using System.IO;
using System.Net;
using System.Text.Json;
using AC_Controller.Models;

namespace AC_Controller.Tests;

public sealed class TrayPreferencesTests
{
    private static readonly GreeSession Device =
        new("001122334455", IPAddress.Loopback, 7000, GreeEncryptionVersion.Ecb, "1234567890123456");

    [Fact]
    public async Task UnreadableSettingsArePreservedWithoutChangingStartupRegistration()
    {
        var errors = new List<Exception>();
        var writes = 0;
        var registryCalls = 0;
        var local = new TrayPreferences(() => throw new JsonException(), _ => registryCalls++,
            _ => writes++, _ => Task.FromResult<GreeSession?>(null), (_, _) => Task.CompletedTask,
            (_, ex) => errors.Add(ex));
        local.Initialize();
        await local.RememberAsync(Device, null, default);
        Assert.Equal(0, writes);
        Assert.Equal(0, registryCalls);
        Assert.Equal(Device.Mac, local.Settings.DeviceMac);
        Assert.IsType<JsonException>(Assert.Single(errors));
    }

    [Fact]
    public void DeniedStartupRegistryKeepsLoadedPreferencesAvailable()
    {
        var errors = new List<Exception>();
        var config = new Config { DeviceMac = Device.Mac, StartWithWindows = true };
        var local = new TrayPreferences(() => config, _ => throw new UnauthorizedAccessException(),
            _ => { }, _ => Task.FromResult<GreeSession?>(null), (_, _) => Task.CompletedTask,
            (_, ex) => errors.Add(ex));
        local.Initialize();
        Assert.Same(config, local.Settings);
        Assert.IsType<UnauthorizedAccessException>(Assert.Single(errors));
    }

    [Fact]
    public async Task DeniedSessionReadAllowsDiscoveryWithoutCache()
    {
        var errors = new List<Exception>();
        var local = new TrayPreferences(() => new Config(), _ => { }, _ => { },
            _ => Task.FromException<GreeSession?>(new UnauthorizedAccessException()),
            (_, _) => Task.CompletedTask, (_, ex) => errors.Add(ex));
        Assert.Null(await local.LoadSessionAsync(default));
        Assert.IsType<UnauthorizedAccessException>(Assert.Single(errors));
    }

    [Fact]
    public async Task FailedLocalWritesPreserveLiveSelectionAndRetryOnNextConnection()
    {
        var errors = new List<Exception>();
        var writes = 0;
        var local = new TrayPreferences(() => new Config(), _ => { },
            _ => { if (++writes == 1) throw new IOException(); },
            _ => Task.FromResult<GreeSession?>(null),
            (_, _) => Task.FromException(new UnauthorizedAccessException()), (_, ex) => errors.Add(ex));
        local.Initialize();
        await local.RememberAsync(Device, null, default);
        Assert.Equal(Device.Mac, local.Settings.DeviceMac);
        Assert.Collection(errors, e => Assert.IsType<UnauthorizedAccessException>(e), e => Assert.IsType<IOException>(e));
        await local.RememberAsync(Device, Device, default);
        Assert.Equal(2, writes);
    }

    [Fact]
    public async Task CancellationDuringCacheWriteDoesNotCommitSettings()
    {
        using var lifetime = new CancellationTokenSource();
        var written = false;
        var local = new TrayPreferences(() => new Config(), _ => { }, _ => written = true,
            _ => Task.FromResult<GreeSession?>(null),
            (_, _) => { lifetime.Cancel(); return Task.CompletedTask; }, (_, _) => { });
        local.Initialize();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => local.RememberAsync(Device, null, lifetime.Token));
        Assert.False(written);
        Assert.Null(local.Settings.DeviceMac);
    }
}
