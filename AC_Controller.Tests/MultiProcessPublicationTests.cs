using System.Diagnostics;
using System.IO;
using AC_Controller.Models;
using AC_Controller.Services;

namespace AC_Controller.Tests;

public sealed class MultiProcessPublicationTests
{
    private const string PeerVariable = "AC_CONTROLLER_TEST_PUBLICATION_PEER";
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task IndependentProcessesPublishCompletePreferences()
    {
        if (Guid.TryParseExact(Environment.GetEnvironmentVariable(PeerVariable), "N", out var peerId))
        {
            using var ready = EventWaitHandle.OpenExisting(EventName(peerId, "ready"));
            using var start = EventWaitHandle.OpenExisting(EventName(peerId, "start"));
            ready.Set();
            Assert.True(start.WaitOne(Deadline));
            WriteAndRead(DirectoryName(peerId), "peer");
            return;
        }

        var id = Guid.NewGuid();
        var directory = Directory.CreateDirectory(DirectoryName(id));
        using var peerReady = new EventWaitHandle(false, EventResetMode.ManualReset, EventName(id, "ready"));
        using var startWrites = new EventWaitHandle(false, EventResetMode.ManualReset, EventName(id, "start"));
        var info = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        info.ArgumentList.Add("vstest");
        info.ArgumentList.Add(typeof(MultiProcessPublicationTests).Assembly.Location);
        info.ArgumentList.Add("--Tests:" + typeof(MultiProcessPublicationTests).FullName + "." +
                              nameof(IndependentProcessesPublishCompletePreferences));
        info.Environment[PeerVariable] = id.ToString("N");
        using var child = Process.Start(info) ?? throw new InvalidOperationException("VSTest indisponível.");
        var output = child.StandardOutput.ReadToEndAsync();
        var errors = child.StandardError.ReadToEndAsync();
        try
        {
            Assert.True(await Task.Run(() => peerReady.WaitOne(Deadline)), "O segundo processo não iniciou a tempo.");
            startWrites.Set();
            WriteAndRead(directory.FullName, "parent");
            await child.WaitForExitAsync().WaitAsync(Deadline);
            Assert.True(child.ExitCode == 0, (await output) + (await errors));
            Assert.Empty(directory.GetFiles("*.tmp"));
        }
        finally
        {
            startWrites.Set();
            if (!child.HasExited)
                child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync();
            directory.Delete(recursive: true);
        }
    }

    private static void WriteAndRead(string directory, string writer)
    {
        var legacy = Path.Combine(directory, "unused-legacy.json");
        for (var index = 0; index < 48; index++)
        {
            var marker = writer == "peer" ? 2 : 1;
            var version = marker * 100 + index;
            ConfigService.Save(directory, new Config { DeviceMac = version.ToString("x12"), LastKnownIp = $"192.0.2.{version}" });
            var read = ConfigService.ReadJson(directory, legacy, ex => throw ex);
            Assert.Equal($"192.0.2.{Convert.ToInt32(read.DeviceMac!, 16)}", read.LastKnownIp);
        }
    }

    private static string DirectoryName(Guid id) =>
        Path.Combine(Path.GetTempPath(), "ACController-process-test-" + id.ToString("N"));

    private static string EventName(Guid id, string phase) => $"ACController_Test_{id:N}_{phase}";
}
