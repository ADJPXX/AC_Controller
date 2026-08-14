namespace AC_Controller.Services;

public static class MenuService
{
    private static string _onOff = "";
    private static string _modo = "";
    private static string _velocidade = "";

    public static async Task Menu()
    {
        GreeService greeService = new();

        while (true)
        {
            var status = await greeService.GetStatusAsync();

            _onOff = status.IsPoweredOn ? "Ligado" : "Desligado";

            _modo = status.Mod == 4 ? "Quente" : "Frio";

            if (status.Tur == 1)
            {
                _velocidade = "Forte";
            }

            _velocidade = status.WdSpd switch
            {
                1 when status.Tur != 1 => "Baixo",
                2 => "Médio-baixo",
                3 => "Médio",
                4 => "Médio-alto",
                5 => "Alto",
                _ => _velocidade
            };

            Console.WriteLine(new string('-', 30));
            Console.WriteLine($"AR CONDICIONADO: {_onOff}");
            if (status.IsPoweredOn)
            {
                Console.WriteLine($"MODO: {_modo}");
                Console.WriteLine($"TEMPERATURA: {status.SetTem}");
                Console.WriteLine($"VELOCIDADE: {_velocidade}");
            }
            Console.WriteLine(new string('-', 30));

            var option = ReadInt(
                "\n[ 0 ]Sair\n[ 1 ]Ligar/Desligar\n[ 2 ]Quente\n[ 3 ]Frio\n[ 4 ]Mudar temperatura\n[ 5 ]Mudar velocidade do vento\n[ 6 ]Mudar direção das aletas\nEscolha sua opção: ");

            Console.Clear();

            if (option == 0)
            {
                break;
            }

            switch (option)
            {
                case 1:
                {
                    if (status.IsPoweredOn)
                    {
                        await greeService.TurnOffAsync();
                    }

                    else
                    {
                        await greeService.TurnOnAsync();
                    }

                    break;
                }

                case 2:
                {
                    if (status.Mod != 4)
                    {
                        await greeService.SetModeAsync(4);
                    }

                    break;
                }

                case 3:
                {
                    if (status.Mod != 1)
                    {
                        await greeService.SetModeAsync(1);
                    }

                    break;
                }

                case 4:
                {
                    var temp = TemperatureMenu();

                    await greeService.SetTemperatureAsync(temp);

                    break;
                }

                case 5:
                {
                    var windSpeed = WindSpeedMenu();

                    await greeService.SetWindSpeedAsync(windSpeed);

                    break;
                }

                default:
                {
                    Console.WriteLine("Opção inválida, tente novamente!");
                    continue;
                }
            }
        }
    }


    private static int TemperatureMenu()
    {
        while (true)
        {
            var temp = ReadInt("Digite uma temperatura entre 16 e 30°C: ");

            if (temp is >= 16 and <= 30)
            {
                return temp;
            }

            Console.Clear();

            Console.WriteLine("Número inválido, tente novamente.\n");
        }
    }


    private static int WindSpeedMenu()
    {
        while (true)
        {
            Console.WriteLine("[ 1 ]Baixo\n[ 2 ]Médio-baixo\n[ 3 ]Médio\n[ 4 ]Médio-alto\n[ 5 ]Alto\n[ 6 ]Forte");

            var option = ReadInt("Digite a velocidade do vento: ");

            if (option is >= 1 and <= 6)
            {
                return option;
            }

            Console.Clear();

            Console.WriteLine("Número inválido, tente novamente.");

        }
    }


    private static int ReadInt(string msg)
    {
        while (true)
        {
            try
            {
                Console.Write(msg);
                if (int.TryParse(Console.ReadLine(), out var result))
                {
                    return result;
                }
            }

            catch
            {
                // ignored
            }
        }
    }
}