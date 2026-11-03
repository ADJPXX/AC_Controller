using System.IO;
using System.Text.Json;
using AC_Controller.Models;

namespace AC_Controller.Services;

public static class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AC_Controller");

    private static string SettingsPath => Path.Combine(DataDirectory, "settings.json");
    private static string LegacyPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AC_Controller.json");

    public static Config ReadJson()
    {
        Directory.CreateDirectory(DataDirectory);

        if (File.Exists(SettingsPath))
        {
            var saved = JsonSerializer.Deserialize<Config>(File.ReadAllText(SettingsPath));
            return saved ?? throw new InvalidOperationException("Não foi possível ler as configurações do AC Controller.");
        }

        var migrated = File.Exists(LegacyPath) ? ReadLegacySettings() : new Config();
        Save(migrated);
        RemoveLegacyPlaintextKey(migrated);
        return migrated;
    }

    public static void Save(Config config)
    {
        Directory.CreateDirectory(DataDirectory);
        var tempPath = SettingsPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(config, JsonOptions));
        File.Move(tempPath, SettingsPath, true);
    }

    private static Config ReadLegacySettings()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(LegacyPath));
        var root = document.RootElement;
        return new Config
        {
            StartWithWindows = root.TryGetProperty("StartWithWindows", out var startup) &&
                               startup.ValueKind == JsonValueKind.True,
            DeviceMac = TryReadString(root, "DeviceMac"),
            LastKnownIp = TryReadString(root, "LastKnownIp")
        };
    }

    private static string? TryReadString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static void RemoveLegacyPlaintextKey(Config migrated)
    {
        if (!File.Exists(LegacyPath))
            return;

        try
        {
            var safeCopy = new Config
            {
                DeviceMac = migrated.DeviceMac,
                LastKnownIp = migrated.LastKnownIp,
                StartWithWindows = migrated.StartWithWindows
            };
            File.WriteAllText(LegacyPath, JsonSerializer.Serialize(safeCopy, JsonOptions));
        }
        catch (IOException)
        {
            // The application may be installed in a read-only directory; settings are already under LocalAppData.
        }
        catch (UnauthorizedAccessException)
        {
            // See the read-only installation case above.
        }
    }
}
