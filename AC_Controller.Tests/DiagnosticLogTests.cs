using System.IO;
using System.Net.Sockets;
using AC_Controller.Services;

namespace AC_Controller.Tests;

public sealed class DiagnosticLogTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("ac-controller-log-tests-");

    [Fact]
    public void RotationKeepsLatestMessagesAndBoundsDiskUse()
    {
        var path = Path.Combine(_directory.FullName, "ac-controller.log");
        var message = new IOException(new string('x', 4000));
        for (var rotation = 0; rotation < 3; rotation++)
        {
            using (var file = File.OpenWrite(path)) file.SetLength(1024 * 1024);
            DiagnosticLog.Write(_directory.FullName, $"ciclo-{rotation}", message);
            Assert.InRange(new FileInfo(path).Length, 1, 2052);
            Assert.Contains($"ciclo-{rotation}", File.ReadAllText(path));
        }
        Assert.Equal(2, _directory.GetFiles().Length);
        Assert.Equal(1024 * 1024, new FileInfo(path + ".1").Length);
    }

    [Fact]
    public void LockedLogDoesNotTakeDownItsCaller()
    {
        var path = Path.Combine(_directory.FullName, "ac-controller.log");
        using (File.Open(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            DiagnosticLog.Write(_directory.FullName, "bloqueado", new IOException());
        DiagnosticLog.Write(_directory.FullName, "liberado", new IOException());
        Assert.Contains("liberado", File.ReadAllText(path));
    }

    [Fact]
    public void AggregateNetworkFailureKeepsItsSocketReasonInTheLog()
    {
        var failure = new SocketException((int)SocketError.NetworkDown);
        DiagnosticLog.Write(_directory.FullName, "descoberta", new IOException("falha", new AggregateException(failure)));
        var path = Path.Combine(_directory.FullName, "ac-controller.log");
        Assert.Contains("SocketException(NetworkDown)", File.ReadAllText(path));
    }

    public void Dispose() => _directory.Delete(true);
}
