using AC_Controller.Models;
using Microsoft.Win32;

namespace AC_Controller.Services;

public class StartupService
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "AC_Controller";

    public void TaskVerification(Config config)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
        UpdateRegistration(config.StartWithWindows, Environment.ProcessPath, key?.GetValue(AppName)?.ToString(),
            SetRegistration, DeleteRegistration);
    }

    internal static void UpdateRegistration(bool enabled, string? executable, string? current, Action<string> set, Action remove)
    {
        if (!enabled)
        {
            if (current is not null)
                remove();
            return;
        }

        if (string.IsNullOrWhiteSpace(executable))
            throw new InvalidOperationException("O caminho do executável do AC Controller está indisponível.");
        var quoted = $"\"{executable}\"";
        if (!string.Equals(quoted, current, StringComparison.Ordinal))
            set(quoted);
    }

    private static void SetRegistration(string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
        key.SetValue(AppName, command);
    }

    private static void DeleteRegistration()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: true);
        key?.DeleteValue(AppName, throwOnMissingValue: false);
    }
}
