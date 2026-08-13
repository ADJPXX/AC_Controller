using System.Text.Json;
using AC_Controller.Models;

namespace AC_Controller.Services;

public sealed class GreeService
{
    private readonly GreeNetworkService _network = new();
    private readonly GreeCryptoService _crypto = new();


    public async Task<GreeStatus> GetStatusAsync()
    {
        var statusPack = CreateStatusPack();

        var encrypted = _crypto.Encrypt(statusPack);

        var request = CreateRequest(encrypted, 0);

        var response = await _network.SendAsync(request);

        using var document = JsonDocument.Parse(response);

        var root = document.RootElement;

        if (!root.TryGetProperty("pack", out var packElement))
        {
            throw new InvalidOperationException("A resposta do Gree não contém 'pack'.");
        }

        if (!root.TryGetProperty("tag", out var tagElement))
        {
            throw new InvalidOperationException("A resposta do Gree não contém 'tag'.");
        }

        var pack = packElement.GetString() ?? string.Empty;

        var tag = tagElement.GetString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(pack))
        {
            throw new InvalidOperationException("O Gree retornou um 'pack' vazio.");
        }

        if (string.IsNullOrWhiteSpace(tag))
        {
            throw new InvalidOperationException("O Gree retornou uma 'tag' vazia.");
        }

        var decryptedResponse = _crypto.Decrypt(pack, tag);

        return ParseStatusResponse(decryptedResponse);
    }


    public Task TurnOnAsync()
    {
        return SetPowerAsync(true);
    }

    public Task TurnOffAsync()
    {
        return SetPowerAsync(false);
    }

    private async Task SetPowerAsync(bool power)
    {
        var commandPack = $$"""{"opt":["Pow"],"p":[{{(power ? 1 : 0)}}],"t":"cmd"}""";

        var encrypted = _crypto.Encrypt(commandPack);

        var request = CreateRequest(encrypted, 0);

        var response = await _network.SendAsync(request);

        ValidateCommandResponse(response);
    }


    public Task SetModeAsync(int mode)
    {
        if (mode is < 0 or > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(mode), "O modo deve estar entre 0 e 4.");
        }

        return SendCommandAsync(["Mod"], [mode]);
    }


    public Task SetTemperatureAsync(int temperature)
    {
        if (temperature is < 16 or > 30)
            throw new ArgumentOutOfRangeException(nameof(temperature), "A temperatura deve estar entre 16 e 30 °C.");

        return SendCommandAsync(["SetTem"], [temperature]);
    }


    private async Task SendCommandAsync(string[] options, int[] values)
    {
        var optionsJson = string.Join(",", options.Select(option => $"\"{option}\""));

        var valuesJson = string.Join(",", values);

        var commandPack = $$"""{"opt":[{{optionsJson}}],"p":[{{valuesJson}}],"t":"cmd"}""";

        var encrypted = _crypto.Encrypt(commandPack);

        var request = CreateRequest(encrypted, 0);

        var response = await _network.SendAsync(request);

        ValidateCommandResponse(response);
    }


    private static string CreateStatusPack()
    {
        var columns = string.Join(",", GreeResources.StatusColumns.Select(column => $"\"{column}\""));

        return $$"""{"cols":[{{columns}}],"mac":"{{GreeResources.Mac}}","t":"status"}""";
    }

    private static string CreateRequest(GcmResult encrypted, int id)
    {
        return $$"""{"cid":"app","i":{{id}},"t":"pack","uid":0,"tcid":"{{GreeResources.Mac}}","tag":"{{encrypted.Tag}}","pack":"{{encrypted.Pack}}"}""";
    }

    private static GreeStatus ParseStatusResponse(string decryptedResponse)
    {
        using var document = JsonDocument.Parse(decryptedResponse);

        var root = document.RootElement;

        if (!root.TryGetProperty("cols", out var colsElement))
        {
            throw new InvalidOperationException("A resposta do Gree não contém 'cols'.");
        }

        if (!root.TryGetProperty("dat", out var datElement))
        {
            throw new InvalidOperationException("A resposta do Gree não contém 'dat'.");
        }

        var columns = colsElement.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToArray();

        var values = datElement.EnumerateArray().ToArray();

        if (columns.Length != values.Length)
        {
            throw new InvalidOperationException("A quantidade de colunas recebidas não corresponde à quantidade de valores.");
        }

        Dictionary<string, JsonElement> data = new();

        for (var i = 0; i < columns.Length; i++)
        {
            data[columns[i]] = values[i];
        }

        return new GreeStatus
        {
            Pow = data["Pow"].GetInt32(),
            Mod = data["Mod"].GetInt32(),
            SetTem = data["SetTem"].GetInt32(),
            TemRec = data["TemRec"].GetInt32(),
            Tur = data["Tur"].GetInt32(),
            Quiet = data["Quiet"].GetInt32(),
            SvSt = data["SvSt"].GetInt32(),
            Lig = data["Lig"].GetInt32()
        };
    }


    private void ValidateCommandResponse(string response)
    {
        using var document = JsonDocument.Parse(response);

        var root = document.RootElement;

        if (!root.TryGetProperty("pack", out var packElement))
        {
            throw new InvalidOperationException("A resposta do Gree não contém 'pack'.");
        }

        if (!root.TryGetProperty("tag", out var tagElement))
        {
            throw new InvalidOperationException("A resposta do Gree não contém 'tag'.");
        }

        var pack = packElement.GetString() ?? string.Empty;
        var tag = tagElement.GetString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(pack) || string.IsNullOrWhiteSpace(tag))
        {
            throw new InvalidOperationException("O Gree retornou uma resposta inválida.");
        }

        var decryptedResponse = _crypto.Decrypt(pack, tag);

        using var decryptedDocument = JsonDocument.Parse(decryptedResponse);

        var decryptedRoot = decryptedDocument.RootElement;

        if (!decryptedRoot.TryGetProperty("r", out var resultElement))
        {
            throw new InvalidOperationException("A resposta descriptografada do Gree não contém 'r'.");
        }

        var result = resultElement.GetInt32();

        if (result != 200)
        {
            throw new InvalidOperationException($"O Gree retornou o código de erro: {result}.");
        }
    }
}