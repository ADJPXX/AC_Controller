using System.Text.Json;
using AC_Controller.Models;

namespace AC_Controller.Services;

public sealed class GreeService
{
    private readonly IGreeTransport _network;

    public GreeService(GreeSession session) : this(session, new GreeNetworkService()) { }

    internal GreeService(GreeSession session, IGreeTransport network)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(network);
        Session = session;
        _network = network;
    }

    public GreeSession Session { get; }

    public async Task<GreeStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var statusPack = CreateStatusPack();
        var encrypted = GreeCryptoService.EncryptDevice(statusPack, Session);
        var request = GreeBindingService.CreateEnvelope(Session.Mac, encrypted, Session.EncryptionVersion, 0);
        var response = await _network.SendAsync(request, Session.IpAddress, Session.Port, cancellationToken);
        var decryptedResponse = DecryptResponse(response);
        return ParseStatusResponse(decryptedResponse);
    }

    public Task TurnOnAsync() => TurnOnAsync(CancellationToken.None);
    public Task TurnOnAsync(CancellationToken cancellationToken) =>
        SendCommandAsync(["Pow"], [1], cancellationToken);

    public Task TurnOffAsync() => TurnOffAsync(CancellationToken.None);
    public Task TurnOffAsync(CancellationToken cancellationToken) =>
        SendCommandAsync(["Pow"], [0], cancellationToken);

    public Task SetModeAsync(int mode) => SetModeAsync(mode, CancellationToken.None);
    public Task SetModeAsync(int mode, CancellationToken cancellationToken)
    {
        if (mode is < 0 or > 4)
            throw new ArgumentOutOfRangeException(nameof(mode), "O modo deve estar entre 0 e 4.");
        return SendCommandAsync(["Mod"], [mode], cancellationToken);
    }

    public Task SetTemperatureAsync(int temperature) => SetTemperatureAsync(temperature, CancellationToken.None);
    public Task SetTemperatureAsync(int temperature, CancellationToken cancellationToken)
    {
        if (temperature is < 16 or > 30)
            throw new ArgumentOutOfRangeException(nameof(temperature), "A temperatura deve estar entre 16 e 30 °C.");
        return SendCommandAsync(["SetTem"], [temperature], cancellationToken);
    }

    public Task SetWindSpeedAsync(int speed) => SetWindSpeedAsync(speed, CancellationToken.None);
    public Task SetWindSpeedAsync(int speed, CancellationToken cancellationToken)
    {
        if (speed is < 1 or > 6)
            throw new ArgumentOutOfRangeException(nameof(speed), "A velocidade deve estar entre 1 e 6.");

        return speed == 6
            ? SendCommandAsync(["WdSpd", "Tur"], [1, 1], cancellationToken)
            : SendCommandAsync(["Tur", "WdSpd"], [0, speed], cancellationToken);
    }

    public Task SetFinDirectionAsync(int direction) => SetFinDirectionAsync(direction, CancellationToken.None);
    public Task SetFinDirectionAsync(int direction, CancellationToken cancellationToken)
    {
        if (direction is < 1 or > 6)
            throw new ArgumentOutOfRangeException(nameof(direction), "A direção deve estar entre 1 e 6.");
        return SendCommandAsync(["SwUpDn"], [direction], cancellationToken);
    }

    private async Task SendCommandAsync(string[] options, int[] values, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var commandPack = JsonSerializer.Serialize(new { opt = options, p = values, t = "cmd" });
        var encrypted = GreeCryptoService.EncryptDevice(commandPack, Session);
        var request = GreeBindingService.CreateEnvelope(Session.Mac, encrypted, Session.EncryptionVersion, 0);
        var response = await _network.SendAsync(request, Session.IpAddress, Session.Port, cancellationToken);
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

    private GreeStatus ParseStatusResponse(string decryptedResponse)
    {
        using var document = JsonDocument.Parse(decryptedResponse);
        var root = document.RootElement;

        ValidateReply(root, "dat", requireResult: false);
        if (!root.TryGetProperty("cols", out var colsElement) || colsElement.ValueKind != JsonValueKind.Array ||
            !root.TryGetProperty("dat", out var dataElement) || dataElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("A resposta de status não contém 'cols' e 'dat' válidos.");

        if (colsElement.GetArrayLength() != dataElement.GetArrayLength())
            throw new InvalidOperationException("A resposta de status tem quantidades diferentes de colunas e valores.");

        var data = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        using var values = dataElement.EnumerateArray();
        foreach (var column in colsElement.EnumerateArray())
        {
            values.MoveNext();
            if (column.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(column.GetString()) ||
                !data.TryAdd(column.GetString()!, values.Current))
                throw new InvalidOperationException("A resposta de status contém uma coluna inválida ou repetida.");
        }

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

        var power = ReadInt("Pow");
        if (power is not (0 or 1))
            throw new InvalidOperationException("O valor de energia retornado deve ser zero ou um.");
        return new GreeStatus
        {
            Pow = power,
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
        ValidateReply(document.RootElement, "res", requireResult: true);
    }

    private void ValidateReply(JsonElement root, string expectedType, bool requireResult)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("A resposta do Gree deve ser um objeto.");

        if (root.TryGetProperty("t", out var type) &&
            (type.ValueKind != JsonValueKind.String || type.GetString() != expectedType))
            throw new InvalidOperationException("O tipo da resposta é incompatível com a operação.");

        if (root.TryGetProperty("mac", out var mac) &&
            (mac.ValueKind != JsonValueKind.String ||
             GreeDiscoveryService.NormalizeMac(mac.GetString()!) != GreeDiscoveryService.NormalizeMac(Session.Mac)))
            throw new InvalidOperationException("A resposta pertence a outro aparelho.");

        if (!root.TryGetProperty("r", out var resultElement))
        {
            if (requireResult) throw new InvalidOperationException("A resposta do comando não contém o código 'r'.");
            return;
        }

        if (resultElement.ValueKind != JsonValueKind.Number || !resultElement.TryGetInt32(out var result))
            throw new InvalidOperationException("A resposta contém um código 'r' inválido.");
        if (result != 200)
            throw new InvalidOperationException($"O Gree retornou o código de erro: {result}.");
    }
}
