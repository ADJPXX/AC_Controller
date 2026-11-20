using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using AC_Controller.Models;

namespace AC_Controller.Services;

public static class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private const int MaxSettingsBytes = 1024 * 1024;

    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AC_Controller");

    public static Config ReadJson() => ReadJson(ex => Trace.TraceWarning("{0}", ex.Message));

    public static Config ReadJson(Action<Exception> reportWarning) =>
        ReadJson(DataDirectory, Path.Combine(AppContext.BaseDirectory, "AC_Controller.json"), reportWarning);

    internal static Config ReadJson(string directory, string legacyPath, Action<Exception> reportWarning)
    {
        ArgumentNullException.ThrowIfNull(reportWarning);
        Directory.CreateDirectory(directory);
        var settingsPath = Path.Combine(directory, "settings.json");
        var settings = ReadTextIfPresent(settingsPath);
        if (settings is not null)
        {
            var current = ParseSettings(settings);
            try
            {
                ReadLegacy(legacyPath, out var legacy, out var needsScrub);
                if (needsScrub)
                    WriteLegacyCopy(legacyPath, legacy);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
            {
                reportWarning(ex);
            }
            return ValidateDevice(current, reportWarning);
        }

        ReadLegacy(legacyPath, out var migrated, out var containsUnknownFields);
        var bytes = SerializeSettings(migrated);
        var published = AtomicFile.Write(settingsPath, bytes, overwrite: false);
        // A concurrent Save or first run may have won publication.
        var result = published ? migrated : ParseSettings(ReadText(settingsPath));
        if (containsUnknownFields)
        {
            try { WriteLegacyCopy(legacyPath, migrated); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { reportWarning(ex); }
        }
        return ValidateDevice(result, reportWarning);
    }

    public static void Save(Config config) => Save(DataDirectory, config);

    internal static void Save(string directory, Config config)
    {
        ArgumentNullException.ThrowIfNull(config);
        Directory.CreateDirectory(directory);
        AtomicFile.Write(Path.Combine(directory, "settings.json"), SerializeSettings(config));
    }

    private static byte[] SerializeSettings(Config config)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(config, JsonOptions);
        if (bytes.Length > MaxSettingsBytes)
            throw new InvalidDataException("As preferências excedem o tamanho permitido.");
        return bytes;
    }

    private static Config ParseSettings(string json) =>
        JsonSerializer.Deserialize<Config>(json) ?? throw new JsonException("O arquivo de configuração contém null.");

    private static Config ValidateDevice(Config config, Action<Exception> reportWarning)
    {
        if (string.IsNullOrWhiteSpace(config.DeviceMac) || GreeDiscoveryService.TryNormalizeMac(config.DeviceMac, out _))
            return config;
        reportWarning(new InvalidDataException("O MAC salvo é inválido. A seleção de aparelho será refeita."));
        return new Config { StartWithWindows = config.StartWithWindows, LastKnownIp = config.LastKnownIp };
    }

    private static string ReadText(string path)
    {
        using var bytes = new MemoryStream(AtomicFile.Read(path, MaxSettingsBytes));
        using var reader = new StreamReader(bytes, new UTF8Encoding(true, true));
        try { return reader.ReadToEnd(); }
        catch (DecoderFallbackException ex) { throw new InvalidDataException("Codificação inválida no arquivo de configuração.", ex); }
    }

    private static string? ReadTextIfPresent(string path)
    {
        try { return ReadText(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    private static void ReadLegacy(string path, out Config settings, out bool needsScrub)
    {
        var text = ReadTextIfPresent(path);
        if (text is null)
        {
            settings = new Config();
            needsScrub = false;
            return;
        }
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("A configuração legada deve ser um objeto JSON.");

        settings = new Config
        {
            StartWithWindows = root.TryGetProperty("StartWithWindows", out var startup) &&
                               startup.ValueKind == JsonValueKind.True,
            DeviceMac = TryReadString(root, "DeviceMac"),
            LastKnownIp = TryReadString(root, "LastKnownIp")
        };
        needsScrub = root.EnumerateObject().Any(property => property.Name switch
        {
            "DeviceMac" or "LastKnownIp" => property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null),
            "StartWithWindows" => property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False),
            _ => true
        });
    }

    private static string? TryReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static void WriteLegacyCopy(string path, Config settings) =>
        AtomicFile.Write(path, SerializeSettings(settings));
}
