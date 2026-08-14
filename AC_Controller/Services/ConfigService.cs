using System.IO;
using System.Text.Json;
using AC_Controller.Models;

namespace AC_Controller.Services;

public static class ConfigService
{
    private static readonly string JsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AC_Controller.json");

    public static Config ReadJson()
    {
        if (!File.Exists(JsonPath))
        {
            CreateDefaultSettings();
        }

        var json = File.ReadAllText(JsonPath);

        var config = JsonSerializer.Deserialize<Config>(json);

        if (config == null)
        {
            throw new Exception("Erro ao carregar as configurações.");
        }

        return config;
    }


    private static void CreateDefaultSettings()
    {
        var configs = new Config
        {
            DeviceKey = "YOUR_DEVICE_KEY_HERE",
            StartWithWindows = false
        };

        var jsonWrite = JsonSerializer.Serialize(configs, new JsonSerializerOptions{WriteIndented = true});

        File.WriteAllText(JsonPath, jsonWrite);
    }
}