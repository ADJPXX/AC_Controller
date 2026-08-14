using System.Text.Json;
using AC_Controller.Models;

namespace AC_Controller.Services;

public sealed class GreeService
{
    private readonly GreeCryptoService _crypto = new();
    private readonly GreeNetworkService _network = new();


    public async Task<GreeStatus> GetStatusAsync()
    {
        var statusPack = CreateStatusPack();

        var encrypted = _crypto.Encrypt(statusPack);

        var request = CreateRequest(encrypted, 0);

        var response = await _network.SendAsync(request);

        using var document = JsonDocument.Parse(response);

        var root = document.RootElement;

        if (!root.TryGetProperty("pack", out var packElement))
            throw new InvalidOperationException("A resposta do Gree não contém 'pack'.");

        if (!root.TryGetProperty("tag", out var tagElement))
            throw new InvalidOperationException("A resposta do Gree não contém 'tag'.");

        var pack = packElement.GetString() ?? string.Empty;

        var tag = tagElement.GetString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(pack)) throw new InvalidOperationException("O Gree retornou um 'pack' vazio.");

        if (string.IsNullOrWhiteSpace(tag)) throw new InvalidOperationException("O Gree retornou uma 'tag' vazia.");

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
        var onOff = power ? 1 : 0;

        await SendCommandAsync(["Pow"], [onOff]);
    }


    public Task SetModeAsync(int mode)
    {
        if (mode is < 0 or > 4) throw new ArgumentOutOfRangeException(nameof(mode), "O modo deve estar entre 0 e 4.");

        return SendCommandAsync(["Mod"], [mode]);
    }


    public Task SetTemperatureAsync(int temperature)
    {
        if (temperature is < 16 or > 30)
            throw new ArgumentOutOfRangeException(nameof(temperature), "A temperatura deve estar entre 16 e 30 °C.");

        return SendCommandAsync(["SetTem"], [temperature]);
    }


    public async Task SetWindSpeedAsync(int speed)
    {
        int tur;

        if (speed is < 1 or > 6)
            throw new ArgumentOutOfRangeException(nameof(speed), "A velocidade deve estar entre 1 e 6.");

        if (speed == 6)
        {
            tur = 1;
            speed = 1;

            await SendCommandAsync(["WdSpd"], [speed]);
            await SendCommandAsync(["Tur"], [tur]);

            return;
        }

        tur = 0;

        await SendCommandAsync(["Tur"], [tur]);
        await SendCommandAsync(["WdSpd"], [speed]);
    }


    public Task SetFinDirectionAsync(int direction)
    {
        if (direction is < 1 or > 6)
            throw new ArgumentOutOfRangeException(nameof(direction), "A velocidade deve estar entre 1 e 6.");

        return SendCommandAsync(["SwUpDn"], [direction]);
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
        return
            $$"""{"cid":"app","i":{{id}},"t":"pack","uid":0,"tcid":"{{GreeResources.Mac}}","tag":"{{encrypted.Tag}}","pack":"{{encrypted.Pack}}"}""";
    }


    private static GreeStatus ParseStatusResponse(string decryptedResponse)
    {
        using var document = JsonDocument.Parse(decryptedResponse);

        var root = document.RootElement;

        if (!root.TryGetProperty("cols", out var colsElement))
            throw new InvalidOperationException("A resposta do Gree não contém 'cols'.");

        if (!root.TryGetProperty("dat", out var datElement))
            throw new InvalidOperationException("A resposta do Gree não contém 'dat'.");

        var columns = colsElement.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToArray();

        var values = datElement.EnumerateArray().ToArray();

        if (columns.Length != values.Length)
            throw new InvalidOperationException(
                "A quantidade de colunas recebidas não corresponde à quantidade de valores.");

        Dictionary<string, JsonElement> data = new();

        for (var i = 0; i < columns.Length; i++) data[columns[i]] = values[i];

        return new GreeStatus
        {
            Pow = data["Pow"].GetInt32(), // AR CONDICIONADO LIGADO OU DESLIGADO (0, 1)
            Mod = data["Mod"].GetInt32(), // MODO: 0 AUTO - 1 ARRE/FRIO - 2 DES - 3 AR - 4 AQUECIMENTO
            SetTem = data["SetTem"].GetInt32(), // TEMPERATURA
            WdSpd = data["WdSpd"].GetInt32(), // VELOCIDADE DO VENTO, VARIA DE 1 ATÉ 5 (1 SE "Tur" ESTIVER LIGADO)
            Blo = data["Blo"].GetInt32(), // FUNÇÃO "Seca"
            Health = data["Health"].GetInt32(), // FUNÇÃO "Saude"
            SwhSlp = data["SwhSlp"].GetInt32(), // FUNÇÃO "Sono"
            Lig = data["Lig"].GetInt32(), // LED (0, 1)
            SwingLfRig =
                data["SwingLfRig"]
                    .GetInt32(), // DIREÇÃO DAS ALETAS (HORIZONTAL) - VALOR MINIMO 2 E VALOR MAXIMO 6, SENDO RESPECTIVAMENTE DIREÇÃO 1 E DIREÇÃO 5 (VALOR 1 FICA MUDANDO DE DIREÇÃO
            SwUpDn = data["SwUpDn"]
                .GetInt32(), // DIREÇÃO DAS ALETAS (VERTICAL) - VALOR MINIMO 2 E VALOR MAXIMO 6, SENDO RESPECTIVAMENTE DIREÇÃO 1 E DIREÇÃO 5 (VALOR 1 FICA MUDANDO DE DIREÇÃO
            Quiet = data["Quiet"].GetInt32(), // VALOR 2 SE A FUNÇÃO "Silen" ATIVADA E 0 SE DESATIVADO.
            Tur = data["Tur"].GetInt32(), // VELOCIDADE DO VENTO "Forte" (0, 1)
            StHt = data["StHt"].GetInt32(), // FUNÇÃO "Aquecimento"
            TemUn = data["TemUn"].GetInt32(), // Mostra graus celsius e graus fahrenheit (0, 1)
            SvSt = data["SvSt"].GetInt32() // FUNÇÃO "Poup" (POUPANÇA/POUPAR)
        };
    }


    private void ValidateCommandResponse(string response)
    {
        using var document = JsonDocument.Parse(response);

        var root = document.RootElement;

        if (!root.TryGetProperty("pack", out var packElement))
            throw new InvalidOperationException("A resposta do Gree não contém 'pack'.");

        if (!root.TryGetProperty("tag", out var tagElement))
            throw new InvalidOperationException("A resposta do Gree não contém 'tag'.");

        var pack = packElement.GetString() ?? string.Empty;
        var tag = tagElement.GetString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(pack) || string.IsNullOrWhiteSpace(tag))
            throw new InvalidOperationException("O Gree retornou uma resposta inválida.");

        var decryptedResponse = _crypto.Decrypt(pack, tag);

        using var decryptedDocument = JsonDocument.Parse(decryptedResponse);

        var decryptedRoot = decryptedDocument.RootElement;

        if (!decryptedRoot.TryGetProperty("r", out var resultElement))
            throw new InvalidOperationException("A resposta descriptografada do Gree não contém 'r'.");

        var result = resultElement.GetInt32();

        if (result != 200) throw new InvalidOperationException($"O Gree retornou o código de erro: {result}.");
    }
}