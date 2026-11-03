using System.Text.Json;
using AC_Controller.Models;

namespace AC_Controller.Services;

public sealed class GreeService(GreeSession session)
{
    private readonly GreeNetworkService _network = new();

    public GreeSession Session { get; } = session;

    public async Task<GreeStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var statusPack = CreateStatusPack();
        var encrypted = GreeCryptoService.EncryptDevice(statusPack, Session);
        var request = GreeBindingService.CreateEnvelope(Session.Mac, encrypted, Session.EncryptionVersion, 0);
        var response = await _network.SendAsync(request, Session.IpAddress, Session.Port, cancellationToken);
        var decryptedResponse = DecryptResponse(response);
        return ParseStatusResponse(decryptedResponse);
    }

    public Task TurnOnAsync() => SetPowerAsync(true);

    public Task TurnOffAsync() => SetPowerAsync(false);

    private Task SetPowerAsync(bool power) => SendCommandAsync(["Pow"], [power ? 1 : 0]);

    public Task SetModeAsync(int mode)
    {
        if (mode is < 0 or > 4)
            throw new ArgumentOutOfRangeException(nameof(mode), "O modo deve estar entre 0 e 4.");
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
        if (speed is < 1 or > 6)
            throw new ArgumentOutOfRangeException(nameof(speed), "A velocidade deve estar entre 1 e 6.");

        if (speed == 6)
        {
            await SendCommandAsync(["WdSpd"], [1]);
            await SendCommandAsync(["Tur"], [1]);
            return;
        }

        await SendCommandAsync(["Tur"], [0]);
        await SendCommandAsync(["WdSpd"], [speed]);
    }

    public Task SetFinDirectionAsync(int direction)
    {
        if (direction is < 1 or > 6)
            throw new ArgumentOutOfRangeException(nameof(direction), "A direção deve estar entre 1 e 6.");
        return SendCommandAsync(["SwUpDn"], [direction]);
    }

    private async Task SendCommandAsync(string[] options, int[] values)
    {
        var commandPack = JsonSerializer.Serialize(new { opt = options, p = values, t = "cmd" });
        var encrypted = GreeCryptoService.EncryptDevice(commandPack, Session);
        var request = GreeBindingService.CreateEnvelope(Session.Mac, encrypted, Session.EncryptionVersion, 0);
        var response = await _network.SendAsync(request, Session.IpAddress, Session.Port);
        ValidateCommandResponse(DecryptResponse(response));
    }

    private string CreateStatusPack() => JsonSerializer.Serialize(new
    {
        cols = GreeResources.StatusColumns,
        mac = Session.Mac,
        t = "status"
    });

    private string DecryptResponse(string response)
    {
        using var document = JsonDocument.Parse(response);
        var root = document.RootElement;

        if (!root.TryGetProperty("pack", out var packElement) || packElement.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("A resposta do Gree não contém um 'pack' válido.");

        string? tag = null;
        if (root.TryGetProperty("tag", out var tagElement) && tagElement.ValueKind == JsonValueKind.String)
            tag = tagElement.GetString();

        var pack = packElement.GetString();
        if (string.IsNullOrWhiteSpace(pack))
            throw new InvalidOperationException("O Gree retornou um pacote vazio.");

        return GreeCryptoService.DecryptDevice(pack, tag, Session);
    }

    private static GreeStatus ParseStatusResponse(string decryptedResponse)
    {
        using var document = JsonDocument.Parse(decryptedResponse);
        var root = document.RootElement;

        if (!root.TryGetProperty("cols", out var colsElement) || colsElement.ValueKind != JsonValueKind.Array ||
            !root.TryGetProperty("dat", out var dataElement) || dataElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("A resposta de status não contém 'cols' e 'dat' válidos.");

        var columns = colsElement.EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString() ?? string.Empty)
            .ToArray();
        var values = dataElement.EnumerateArray().ToArray();
        if (columns.Length != values.Length)
            throw new InvalidOperationException("A resposta de status tem quantidades diferentes de colunas e valores.");

        var data = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        for (var i = 0; i < columns.Length; i++)
            data[columns[i]] = values[i];

        int ReadInt(string name, int defaultValue = 0)
        {
            if (!data.TryGetValue(name, out var value))
            {
                if (name is "Pow" or "Mod" or "SetTem" or "WdSpd")
                    throw new InvalidOperationException($"A resposta de status não contém o campo obrigatório '{name}'.");
                return defaultValue;
            }

            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var parsed))
                throw new InvalidOperationException($"A resposta de status não contém um valor numérico válido para '{name}'.");
            return parsed;
        }

        return new GreeStatus
        {
            Pow = ReadInt("Pow"),
            Mod = ReadInt("Mod"),
            SetTem = ReadInt("SetTem"),
            WdSpd = ReadInt("WdSpd"),
            Blo = ReadInt("Blo"),
            Health = ReadInt("Health"),
            SwhSlp = ReadInt("SwhSlp"),
            Lig = ReadInt("Lig"),
            SwingLfRig = ReadInt("SwingLfRig"),
            SwUpDn = ReadInt("SwUpDn"),
            Quiet = ReadInt("Quiet"),
            Tur = ReadInt("Tur"),
            StHt = ReadInt("StHt"),
            TemUn = ReadInt("TemUn"),
            SvSt = ReadInt("SvSt")
        };
    }

    private void ValidateCommandResponse(string decryptedResponse)
    {
        using var document = JsonDocument.Parse(decryptedResponse);
        var root = document.RootElement;

        if (!root.TryGetProperty("r", out var resultElement) ||
            resultElement.ValueKind != JsonValueKind.Number ||
            !resultElement.TryGetInt32(out var result))
            throw new InvalidOperationException("A resposta descriptografada do Gree não contém um código 'r' válido.");

        if (result != 200)
            throw new InvalidOperationException($"O Gree retornou o código de erro: {result}.");
    }
}
