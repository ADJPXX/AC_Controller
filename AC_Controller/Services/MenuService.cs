namespace AC_Controller.Services;

public static class MenuService
{
    private static string _onOff = "";
    private static string _modo = "";

    public static async Task Menu()
    {
        GreeService greeService = new();

        while (true)
        {
            var status = await greeService.GetStatusAsync();

            if (status.IsPoweredOn)
            {
                _onOff = "SIM";
            }
            else
            {
                _onOff = "NÃO";
            }

            if (status.Mod == 4)
            {
                _modo = "Quente";
            }
            else
            {
                _modo = "Frio";
            }

            Console.WriteLine(new string('-', 30));
            Console.WriteLine($"LIGADO: {_onOff}");
            Console.WriteLine($"MODO: {_modo}");
            Console.WriteLine($"TEMPERATURA CONFIGURADA: {status.SetTem}");
            Console.WriteLine($"TEMPERATURA AMBIENTE: {status.TemRec}");
            Console.WriteLine($"VELOCIDADE: {status.WdSpd}");
            Console.WriteLine(new string('-', 30));

            var option = ReadInt("\n[ 0 ]Sair\n[ 1 ]Ligar/Desligar\n[ 2 ]Quente\n[ 3 ]Frio\n[ 4 ]Mudar temperatura\nEscolha sua opção: ");

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

                    Console.Clear();
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
                    Console.Clear();
                    var temp = MenuTemperature();

                    await greeService.SetTemperatureAsync(temp);

                    break;
                }
            }
        }
    }


    private static int MenuTemperature()
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