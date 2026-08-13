using System.Text.Json;
using AC_Controller.Models;

namespace AC_Controller.Services;

public sealed class GreeService
{
    private readonly GreeNetworkService _network;
    private readonly GreeCryptoService _crypto;

    public GreeService()
    {
        _network = new GreeNetworkService();
        _crypto = new GreeCryptoService();
    }


    public async Task<GreeStatus> GetStatusAsync()
    {
        string statusPack =
            CreateStatusPack();

        GcmResult encrypted =
            _crypto.Encrypt(statusPack);

        string request =
            CreateRequest(
                encrypted,
                0);

        string response =
            await _network.SendAsync(request);

        using JsonDocument document =
            JsonDocument.Parse(response);

        JsonElement root =
            document.RootElement;

        if (!root.TryGetProperty(
                "pack",
                out JsonElement packElement))
        {
            throw new InvalidOperationException(
                "A resposta do Gree não contém 'pack'.");
        }

        if (!root.TryGetProperty(
                "tag",
                out JsonElement tagElement))
        {
            throw new InvalidOperationException(
                "A resposta do Gree não contém 'tag'.");
        }

        string pack =
            packElement.GetString() ?? string.Empty;

        string tag =
            tagElement.GetString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(pack))
        {
            throw new InvalidOperationException(
                "O Gree retornou um 'pack' vazio.");
        }

        if (string.IsNullOrWhiteSpace(tag))
        {
            throw new InvalidOperationException(
                "O Gree retornou uma 'tag' vazia.");
        }

        string decryptedResponse =
            _crypto.Decrypt(
                pack,
                tag);

        return ParseStatusResponse(
            decryptedResponse);
    }


    public Task TurnOnAsync()
    {
        return SetPowerAsync(true);
    }

    public Task TurnOffAsync()
    {
        return SetPowerAsync(false);
    }

    public async Task SetPowerAsync(bool power)
    {
        string commandPack =
            $$"""{"opt":["Pow"],"p":[{{(power ? 1 : 0)}}],"t":"cmd"}""";

        GcmResult encrypted =
            _crypto.Encrypt(commandPack);

        string request =
            CreateRequest(
                encrypted,
                0);

        string response =
            await _network.SendAsync(request);

        ValidateCommandResponse(response);
    }

    private static string CreateStatusPack()
    {
        string columns =
            string.Join(
                ",",
                GreeResources.StatusColumns
                    .Select(
                        column => $"\"{column}\""));

        return
            $$"""{"cols":[{{columns}}],"mac":"{{GreeResources.Mac}}","t":"status"}""";
    }

    private static string CreateRequest(
        GcmResult encrypted,
        int id)
    {
        return
            $$"""{"cid":"app","i":{{id}},"t":"pack","uid":0,"tcid":"{{GreeResources.Mac}}","tag":"{{encrypted.Tag}}","pack":"{{encrypted.Pack}}"}""";
    }

    private static GreeStatus ParseStatusResponse(string decryptedResponse)
    {
        using JsonDocument document = JsonDocument.Parse(decryptedResponse);

        JsonElement root = document.RootElement;

        if (!root.TryGetProperty("cols", out JsonElement colsElement))
            throw new InvalidOperationException("A resposta do Gree não contém 'cols'.");

        if (!root.TryGetProperty("dat", out JsonElement datElement))
            throw new InvalidOperationException("A resposta do Gree não contém 'dat'.");

        string[] columns = colsElement
            .EnumerateArray()
            .Select(x => x.GetString() ?? string.Empty)
            .ToArray();

        JsonElement[] values = datElement
            .EnumerateArray()
            .ToArray();

        if (columns.Length != values.Length)
            throw new InvalidOperationException(
                "A quantidade de colunas recebidas não corresponde à quantidade de valores."
            );

        Dictionary<string, JsonElement> data = new();

        for (int i = 0; i < columns.Length; i++)
            data[columns[i]] = values[i];

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
        using JsonDocument document = JsonDocument.Parse(response);

        JsonElement root = document.RootElement;

        if (!root.TryGetProperty("pack", out JsonElement packElement))
            throw new InvalidOperationException("A resposta do Gree não contém 'pack'.");

        if (!root.TryGetProperty("tag", out JsonElement tagElement))
            throw new InvalidOperationException("A resposta do Gree não contém 'tag'.");

        string pack = packElement.GetString() ?? string.Empty;
        string tag = tagElement.GetString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(pack) || string.IsNullOrWhiteSpace(tag))
            throw new InvalidOperationException("O Gree retornou uma resposta inválida.");

        string decryptedResponse = _crypto.Decrypt(pack, tag);

        using JsonDocument decryptedDocument =
            JsonDocument.Parse(decryptedResponse);

        JsonElement decryptedRoot = decryptedDocument.RootElement;

        if (!decryptedRoot.TryGetProperty("r", out JsonElement resultElement))
            throw new InvalidOperationException(
                "A resposta descriptografada do Gree não contém 'r'."
            );

        int result = resultElement.GetInt32();

        if (result != 200)
            throw new InvalidOperationException(
                $"O Gree retornou o código de erro: {result}."
            );
    }
}