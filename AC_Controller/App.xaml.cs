using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AC_Controller.Models;
using AC_Controller.Services;
using Hardcodet.Wpf.TaskbarNotification;

namespace AC_Controller;

public partial class App
{
    private GreeService? _greeService;
    private GreeStatus? _status;
    private TaskbarIcon? _trayIcon;
    private bool _isConnected;
    private Stream? _iconStream;
    private Config? _config;
    private readonly StartupService _startupService = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _config = ConfigService.ReadJson();

        _startupService.TaskVerification(_config);

        try
        {
            _greeService = new GreeService(_config);

            _iconStream = typeof(App).Assembly.GetManifestResourceStream(
                              "AC_Controller.Resources.accontroller.ico")
                          ?? throw new InvalidOperationException(
                              "Não foi possível carregar o ícone incorporado.");

            _trayIcon = new TaskbarIcon
            {
                Icon = new Icon(_iconStream),
                ToolTipText = "AC Controller"
            };

            _ = UpdateStatusAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.ToString(),
                "AC Controller",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown();
        }
    }


    private async Task UpdateStatusAsync()
    {
        while (_trayIcon != null)
        {
            try
            {
                _status = await _greeService!.GetStatusAsync();

                _isConnected = true;

                await Dispatcher.InvokeAsync(UpdateTrayMenu);
            }
            catch
            {
                _isConnected = false;

                await Dispatcher.InvokeAsync(UpdateTrayMenu);
            }

            await Task.Delay(3000);
        }
    }


    private void UpdateTrayMenu()
    {
        if (!_isConnected || _status is null)
        {
            _trayIcon!.ToolTipText = "AC Controller - Sem conexão";

            _trayIcon.ContextMenu = new ContextMenu
            {
                Items =
                {
                    new MenuItem
                    {
                        Header = "Ar-condicionado desconectado",
                        IsEnabled = false
                    },

                    new Separator(),

                    new MenuItem
                    {
                        Header = "Sair",
                        Command = ApplicationCommands.Close
                    }
                }
            };

            return;
        }

        _trayIcon?.ToolTipText =
            _status.IsPoweredOn
                ? $"AC Controller - Ligado ({_status.SetTem}°C)"
                : "AC Controller - Desligado";

        if (_trayIcon == null || _status == null)
            return;

        var menu = new ContextMenu();

        var statusItem = new MenuItem
        {
            Header = _status.IsPoweredOn
                ? "Ar condicionado: Ligado"
                : "Ar condicionado: Desligado",

            IsEnabled = false
        };

        menu.Items.Add(statusItem);

        if (_status.IsPoweredOn)
        {
            menu.Items.Add(
                new MenuItem
                {
                    Header = $"Modo: {GetModeName()}",
                    IsEnabled = false
                });

            menu.Items.Add(
                new MenuItem
                {
                    Header = $"Temperatura: {_status.SetTem}°C",
                    IsEnabled = false
                });

            menu.Items.Add(
                new MenuItem
                {
                    Header = $"Velocidade: {GetSpeedName()}",
                    IsEnabled = false
                });

            menu.Items.Add(
                new MenuItem
                {
                    Header = $"Direção: {GetFinDirectionName()}",
                    IsEnabled = false
                });
        }

        menu.Items.Add(new Separator());

        menu.Items.Add(CreatePowerMenu());

        if (_status.IsPoweredOn)
        {
            menu.Items.Add(CreateModeMenu());
            menu.Items.Add(CreateTemperatureMenu());
            menu.Items.Add(CreateWindSpeedMenu());
            menu.Items.Add(CreateFinDirectionMenu());
        }

        menu.Items.Add(new Separator());

        var exit = new MenuItem
        {
            Header = "Sair"
        };

        exit.Click += (_, _) => Shutdown();

        menu.Items.Add(exit);

        _trayIcon.ContextMenu = menu;

        _trayIcon.ToolTipText = BuildTooltip();
    }


    private MenuItem CreatePowerMenu()
    {
        var item = new MenuItem
        {
            Header = _status!.IsPoweredOn
                ? "Desligar"
                : "Ligar"
        };

        item.Click += async (_, _) =>
        {
            if (_status!.IsPoweredOn)
                await _greeService!.TurnOffAsync();
            else
                await _greeService!.TurnOnAsync();

            await RefreshStatusAsync();
        };

        return item;
    }


    private MenuItem CreateModeMenu()
    {
        var menu = new MenuItem
        {
            Header = "Modo"
        };

        var heat = new MenuItem
        {
            Header = "Quente",
            IsChecked = _status!.Mod == 4
        };

        heat.Click += async (_, _) =>
        {
            if (_status!.Mod != 4)
                await _greeService!.SetModeAsync(4);

            await RefreshStatusAsync();
        };

        var cool = new MenuItem
        {
            Header = "Frio",
            IsChecked = _status.Mod == 1
        };

        cool.Click += async (_, _) =>
        {
            if (_status!.Mod != 1)
                await _greeService!.SetModeAsync(1);

            await RefreshStatusAsync();
        };

        menu.Items.Add(heat);
        menu.Items.Add(cool);

        return menu;
    }


    private MenuItem CreateTemperatureMenu()
    {
        var menu = new MenuItem
        {
            Header = "Temperatura"
        };

        for (var temperature = 16; temperature <= 30; temperature++)
        {
            var selectedTemperature = temperature;

            var item = new MenuItem
            {
                Header = $"{temperature} °C",
                IsChecked = _status!.SetTem == temperature
            };

            item.Click += async (_, _) =>
            {
                await _greeService!.SetTemperatureAsync(
                    selectedTemperature);

                await RefreshStatusAsync();
            };

            menu.Items.Add(item);
        }

        return menu;
    }


    private MenuItem CreateWindSpeedMenu()
    {
        var menu = new MenuItem
        {
            Header = "Velocidade"
        };

        var speeds = new Dictionary<int, string>
        {
            [1] = "Baixo",
            [2] = "Médio-baixo",
            [3] = "Médio",
            [4] = "Médio-alto",
            [5] = "Alto",
            [6] = "Forte"
        };

        foreach (var speed in speeds)
        {
            var selectedSpeed = speed.Key;

            var item = new MenuItem
            {
                Header = speed.Value,

                IsChecked = selectedSpeed == 6
                    ? _status!.Tur == 1
                    : _status!.Tur == 0 &&
                      _status.WdSpd == selectedSpeed
            };

            item.Click += async (_, _) =>
            {
                await _greeService!.SetWindSpeedAsync(
                    selectedSpeed);

                await RefreshStatusAsync();
            };

            menu.Items.Add(item);
        }

        return menu;
    }


    private MenuItem CreateFinDirectionMenu()
    {
        var menu = new MenuItem
        {
            Header = "Direção das aletas"
        };

        var directions = new Dictionary<int, string>
        {
            [2] = "Direção 1",
            [3] = "Direção 2",
            [4] = "Direção 3",
            [5] = "Direção 4",
            [6] = "Direção 5",
            [1] = "Ficar mudando de direção"
        };

        foreach (var direction in directions)
        {
            var selectedDirection = direction.Key;

            var item = new MenuItem
            {
                Header = direction.Value,
                IsChecked = _status!.SwUpDn == selectedDirection
            };

            item.Click += async (_, _) =>
            {
                await _greeService!.SetFinDirectionAsync(
                    selectedDirection);

                await RefreshStatusAsync();
            };

            menu.Items.Add(item);
        }

        return menu;
    }


    private string GetModeName()
    {
        return _status!.Mod switch
        {
            0 => "Auto",
            1 => "Frio",
            2 => "Desumidificar",
            3 => "Ventilar",
            4 => "Quente",
            _ => "Desconhecido"
        };
    }


    private string GetSpeedName()
    {
        if (_status!.Tur == 1)
            return "Forte";

        return _status.WdSpd switch
        {
            1 => "Baixo",
            2 => "Médio-baixo",
            3 => "Médio",
            4 => "Médio-alto",
            5 => "Alto",
            _ => "Desconhecido"
        };
    }


    private string GetFinDirectionName()
    {
        return _status!.SwUpDn switch
        {
            1 => "Ficar mudando de direção",
            2 => "Direção 1",
            3 => "Direção 2",
            4 => "Direção 3",
            5 => "Direção 4",
            6 => "Direção 5",
            _ => "Desconhecida"
        };
    }


    private string BuildTooltip()
    {
        if (_status == null)
            return "AC Controller";

        if (!_status.IsPoweredOn)
            return "AC Controller\nDesligado";

        return
            $"AC Controller\n" +
            $"Ligado\n" +
            $"Modo: {GetModeName()}\n" +
            $"Temperatura: {_status.SetTem}°C\n" +
            $"Velocidade: {GetSpeedName()}\n" +
            $"Direção: {GetFinDirectionName()}";
    }


    private async Task RefreshStatusAsync()
    {
        _status = await _greeService!.GetStatusAsync();

        await Dispatcher.InvokeAsync(UpdateTrayMenu);
    }


    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _iconStream?.Dispose();

        base.OnExit(e);
    }
}