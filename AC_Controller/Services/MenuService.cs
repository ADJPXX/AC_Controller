namespace AC_Controller.Services;

public static class MenuService
{
    private static string _onOff = "";
    private static string _mode = "";
    private static string _speed = "";
    private static string _finDirection = "";
    private static string _warning = string.Empty;

    public static async Task Menu()
    {
        GreeService greeService = new();

        while (true)
        {
            var status = await greeService.GetStatusAsync();

            _onOff = status.IsPoweredOn ? "Ligado" : "Desligado";

            _mode = status.Mod == 4 ? "Quente" : "Frio";

            if (status.Tur == 1)
            {
                _speed = "Forte";
            }

            _speed = status.WdSpd switch
            {
                1 when status.Tur != 1 => "Baixo",
                2 => "Médio-baixo",
                3 => "Médio",
                4 => "Médio-alto",
                5 => "Alto",
                _ => _speed
            };

            _finDirection = status.SwUpDn switch
            {
                1 => "Ficar mudando de direção",
                2 => "Direção 1",
                3 => "Direção 2",
                4 => "Direção 3",
                5 => "Direção 4",
                6 => "Direção 5",
                _ => _finDirection
            };
            
            Console.WriteLine(new string('-', 30));
            Console.WriteLine($"AR CONDICIONADO: {_onOff}");
            if (status.IsPoweredOn)
            {
                Console.WriteLine($"MODO: {_mode}");
                Console.WriteLine($"TEMPERATURA: {status.SetTem}");
                Console.WriteLine($"VELOCIDADE: {_speed}");
                Console.WriteLine($"DIREÇÃO DAS ALETAS: {_finDirection}");
            }
            Console.WriteLine(new string('-', 30));

            var option = ReadInt(
                "[ 0 ]Sair\n[ 1 ]Ligar/Desligar\n[ 2 ]Quente\n[ 3 ]Frio\n[ 4 ]Mudar temperatura\n[ 5 ]Mudar velocidade do vento\n[ 6 ]Mudar direção das aletas\nEscolha sua opção: ");

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

                case 6:
                {
                    var finDirection = FinDirectionMenu();

                    await greeService.SetFinDirectionAsync(finDirection);

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


    private static int FinDirectionMenu()
    {
        while (true)
        {
            Console.WriteLine("[ 1 ]Direção 1\n[ 2 ]Direção 2\n[ 3 ]Direção 3\n[ 4 ]Direção 4\n[ 5 ]Direção 5\n[ 6 ]Ficar mudando de direção");

            var option = ReadInt("Escolha sua opção: ");

            if (option is >= 1 and <= 6)
            {
                if (option == 6)
                {
                    return option - 5;
                }

                return option + 1;
            }

            Console.Clear();

            Console.WriteLine("Número inválido, digite novamente.");
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