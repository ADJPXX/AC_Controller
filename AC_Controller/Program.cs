using AC_Controller.Services;

namespace AC_Controller;

public static class Program
{
    public static async Task Main()
    {
        await MenuService.Menu();
    }
}