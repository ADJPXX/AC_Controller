using System.ComponentModel;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text.Json;
using AC_Controller.Models;

namespace AC_Controller;

internal sealed class TrayPreferences(
    Func<Config> readSettings,
    Action<Config> reconcileStartup,
    Action<Config> writeSettings,
    Func<CancellationToken, Task<GreeSession?>> loadSession,
    Func<GreeSession, CancellationToken, Task> saveSession,
    Action<string, Exception> report)
{
    public Config Settings { get; private set; } = new();
    private bool _settingsReadable;
    private bool _pendingWrite;

    public void Initialize()
    {
        try
        {
            Settings = readSettings();
            _settingsReadable = true;
        }
        catch (Exception ex) when (IsLocalDataFailure(ex))
        {
            report("Não foi possível ler as preferências", ex);
            return;
        }

        try
        {
            reconcileStartup(Settings);
        }
        catch (Exception ex) when (IsLocalDataFailure(ex))
        {
            report("Não foi possível configurar a inicialização com o Windows", ex);
        }
    }

    public async Task<GreeSession?> LoadSessionAsync(CancellationToken token)
    {
        try
        {
            return await loadSession(token);
        }
        catch (Exception ex) when (IsLocalDataFailure(ex))
        {
            report("Não foi possível ler a sessão salva", ex);
            return null;
        }
    }

    public async Task RememberAsync(GreeSession session, GreeSession? cached, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (session != cached)
        {
            try
            {
                await saveSession(session, token);
            }
            catch (Exception ex) when (IsLocalDataFailure(ex))
            {
                report("Não foi possível salvar a sessão do aparelho", ex);
            }
        }
        token.ThrowIfCancellationRequested();
        if (Settings.DeviceMac != session.Mac || Settings.LastKnownIp != session.IpAddress.ToString())
        {
            Settings = new Config
            {
                DeviceMac = session.Mac,
                LastKnownIp = session.IpAddress.ToString(),
                StartWithWindows = Settings.StartWithWindows
            };
            _pendingWrite = true;
        }
        if (_settingsReadable && _pendingWrite)
        {
            try
            {
                writeSettings(Settings);
                _pendingWrite = false;
            }
            catch (Exception ex) when (IsLocalDataFailure(ex))
            {
                report("Não foi possível salvar as preferências do aparelho", ex);
            }
        }
    }

    private static bool IsLocalDataFailure(Exception ex) =>
        ex is IOException or InvalidDataException or UnauthorizedAccessException or SecurityException or Win32Exception or
            CryptographicException or JsonException or InvalidOperationException or FormatException;
}
