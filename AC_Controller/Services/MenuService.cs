namespace AC_Controller.Services;

public static class MenuService
{
    private static string _onOff = "";
    private static string _mode = "";
    private static string _speed = "";
    private static string _finDirection = "";
    private static readonly string Lines = new('-', 30);

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

            var menu = $"""
                         {Lines}
                         AR CONDICIONADO: {_onOff}
                         """;

            if (status.IsPoweredOn)
            {
                menu += $"""
                         
                         MODO: {_mode}
                         TEMPERATURA: {status.SetTem}
                         VELOCIDADE: {_speed}
                         DIREÇÃO DAS ALETAS: {_finDirection}
                         """;
            }

            menu += $"\n{Lines}\n";

            menu += """
                       [ 0 ]Sair
                       [ 1 ]Ligar/Desligar
                       """;

            if (status.IsPoweredOn)
            {
                menu += """
                        
                        [ 2 ]Quente
                        [ 3 ]Frio
                        [ 4 ]Mudar temperatura
                        [ 5 ]Mudar velocidade do vento
                        [ 6 ]Mudar direção das aletas
                        """;
            }

            menu += "\nEscolha sua opção: ";

            var option = ReadInt(menu);

            Console.Clear();

            if (option == -1)
            {
                Console.Clear();

                Console.WriteLine("Opção inválida, tente novamente.");

                continue;
            }

            if (option == 0)
            {
                break;
            }

            if (!status.IsPoweredOn && option is >= 2 and <= 6)
            {
                Console.WriteLine("O ar condicionado está desligado, ligue ele para que você consiga executar qualquer comando.");
                continue;
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

            if (temp == -1)
            {
                Console.Clear();

                Console.WriteLine("Temperatura inválida, tente novamente.");

                continue;
            }

            if (temp is >= 16 and <= 30)
            {
                Console.Clear();

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

            if (option == -1)
            {
                Console.Clear();

                Console.WriteLine("Velocidade inválida, tente novamente.");

                continue;
            }

            if (option is >= 1 and <= 6)
            {
                Console.Clear();

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

            if (option == -1)
            {
                Console.Clear();

                Console.WriteLine("Opção inválida, tente novamente.");

                continue;
            }

            if (option is >= 1 and <= 6)
            {
                if (option == 6)
                {
                    Console.Clear();

                    return option - 5;
                }

                Console.Clear();

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
            Console.Write(msg);
            if (int.TryParse(Console.ReadLine(), out var result))
            {
                return result;
            }

            return -1;
        }
    }
}