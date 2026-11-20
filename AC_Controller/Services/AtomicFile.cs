using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AC_Controller.Services;

internal static class AtomicFile
{
    public static byte[] Read(string path, int maxBytes, CancellationToken token = default) =>
        WithLock(path, token, () =>
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var length = stream.Length;
            if (length > maxBytes)
                throw new InvalidDataException("O arquivo de dados excede o tamanho permitido.");
            var bytes = new byte[(int)length];
            for (var offset = 0; offset < bytes.Length;)
            {
                token.ThrowIfCancellationRequested();
                var count = Math.Min(bytes.Length - offset, 64 * 1024);
                stream.ReadExactly(bytes, offset, count);
                offset += count;
            }
            return bytes;
        });

    public static bool Write(string path, byte[] contents, bool overwrite = true)
    {
        var temporary = NewTemporaryPath(path);
        using var owned = new TemporaryFile(temporary, asynchronous: false);
        owned.Stream.Write(contents);
        try { owned.Publish(path, overwrite, CancellationToken.None); }
        catch (IOException ex) when (!overwrite && (ex.HResult & 0xffff) is 80 or 183)
        {
            // Another process published its settings first. Its version is authoritative.
            return false;
        }
        return true;
    }

    public static async Task WriteAsync(string path, byte[] contents, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var temporary = NewTemporaryPath(path);
        using var owned = new TemporaryFile(temporary, asynchronous: true);
        await owned.Stream.WriteAsync(contents, cancellationToken).ConfigureAwait(false);
        await owned.Stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        owned.Publish(path, overwrite: true, cancellationToken);
    }

    private static string NewTemporaryPath(string path) =>
        path + "." + Guid.NewGuid().ToString("N") + ".tmp";

    private static T WithLock<T>(string path, CancellationToken token, Func<T> action)
    {
        var fullPath = Path.GetFullPath(path).ToUpperInvariant();
        var name = "AC_Controller_File_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullPath)));
        var options = new NamedWaitHandleOptions { CurrentUserOnly = true, CurrentSessionOnly = false };
        using var mutex = new Mutex(false, name, options);
        var timer = Stopwatch.StartNew();
        try
        {
            while (!mutex.WaitOne(TimeSpan.FromMilliseconds(50)))
            {
                token.ThrowIfCancellationRequested();
                if (timer.Elapsed >= TimeSpan.FromSeconds(3))
                    throw new IOException("Tempo esgotado ao aguardar outra instância do AC Controller.");
            }
        }
        catch (AbandonedMutexException)
        {
            // The previous owner exited. Windows granted this thread ownership.
        }
        try
        {
            token.ThrowIfCancellationRequested();
            return action();
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private sealed class TemporaryFile(string path, bool asynchronous) : IDisposable
    {
        private bool _published;
        public FileStream Stream { get; } =
            new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, asynchronous);

        public void Publish(string destination, bool overwrite, CancellationToken token)
        {
            Stream.Dispose();
            _published = WithLock(destination, token, () =>
            {
                File.Move(path, destination, overwrite);
                return true;
            });
        }

        public void Dispose()
        {
            try { Stream.Dispose(); }
            finally
            {
                if (!_published)
                    File.Delete(path);
            }
        }
    }
}
