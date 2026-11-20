using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace AC_Controller.Services;

internal static class DiagnosticLog
{
    private const int FileLimit = 1024 * 1024;
    private static readonly object Gate = new();

    public static void Write(string context, Exception error) =>
        Write(ConfigService.DataDirectory, context, error);

    internal static void Write(string directory, string context, Exception error)
    {
        try
        {
            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} {context}: {error.GetType().Name}: {error.Message}";
            var cause = error.InnerException;
            if (cause is not null)
            {
                var causes = cause is AggregateException group ? group.InnerExceptions.AsEnumerable() : [cause];
                var reasons = causes.Take(4).Select(ex => ex is SocketException socket
                    ? $"{nameof(SocketException)}({socket.SocketErrorCode})"
                    : $"{ex.GetType().Name}(0x{ex.HResult:X8})");
                line += $" [{string.Join(", ", reasons)}]";
            }
            if (line.Length > 2048) line = line[..2048];
            var bytes = Encoding.UTF8.GetBytes(line + Environment.NewLine);
            lock (Gate)
            {
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "ac-controller.log");
                var current = new FileInfo(path);
                if (current.Exists && current.Length + bytes.Length > FileLimit)
                    File.Move(path, path + ".1", overwrite: true);
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                stream.Write(bytes);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Trace.WriteLine($"AC Controller: falha ao registrar diagnóstico ({ex.GetType().Name}).");
        }
    }
}
