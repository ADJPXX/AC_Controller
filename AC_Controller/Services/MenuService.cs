namespace AC_Controller.Services;

public static class MenuService
{
    public static async Task Menu()
    {
        GreeService greeService = new();

        while (true)
        {
            var status = await greeService.GetStatusAsync();

            Console.WriteLine(new string('-', 30));
            Console.WriteLine($"LIGADO: {status.IsPoweredOn}");
            Console.WriteLine($"MODO: {status.Mod}");
            Console.WriteLine($"TEMPERATURA CONFIGURADA: {status.SetTem}");
            Console.WriteLine($"TEMPERATURA AMBIENTE: {status.TemRec}");
            Console.WriteLine($"VELOCIDADE: {status.WdSpd}");
            Console.WriteLine(new string('-', 30));

            var option = ReadInt("\n[ 0 ]Sair\n[ 1 ]Ligar/Desligar\nEscolha sua opção: ");

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
            }
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