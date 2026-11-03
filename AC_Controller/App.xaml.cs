using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
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
    private Icon? _trayIconImage;
    private Config _config = new();
    private string _connectionMessage = "Procurando o ar-condicionado na rede local…";

    private readonly StartupService _startupService = new();
    private readonly GreeDiscoveryService _discoveryService = new();
    private readonly GreeNetworkService _networkService = new();
    private readonly GreeSessionStore _sessionStore = new();
    private readonly SemaphoreSlim _reconnectSignal = new(0, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private int _forceDeviceSelection;
    private bool _selectionDeclined;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _config = ConfigService.ReadJson();
            _startupService.TaskVerification(_config);

            _iconStream = typeof(App).Assembly.GetManifestResourceStream("AC_Controller.Resources.accontroller.ico")
                          ?? throw new InvalidOperationException("Não foi possível carregar o ícone do aplicativo.");
            _trayIconImage = new Icon(_iconStream);
            _trayIcon = new TaskbarIcon
            {
                Icon = _trayIconImage,
                ToolTipText = "AC Controller - procurando o aparelho"
            };

            UpdateTrayMenu();
            _ = RunConnectionLoopAsync(_shutdown.Token);
        }
        catch (Exception ex)
        {
            LogException("Falha ao iniciar", ex);
            MessageBox.Show(ex.Message, "AC Controller", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private async Task RunConnectionLoopAsync(CancellationToken cancellationToken)
    {
        var retryDelay = TimeSpan.FromSeconds(4);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                _isConnected = false;
                _status = null;
                _greeService = null;
                _connectionMessage = "Procurando o ar-condicionado na rede local…";
                await Dispatcher.InvokeAsync(UpdateTrayMenu);

                var (service, status) = await ConnectAsync(cancellationToken);
                _greeService = service;
                _status = status;
                _isConnected = true;
                _connectionMessage = string.Empty;
                retryDelay = TimeSpan.FromSeconds(4);
                await Dispatcher.InvokeAsync(UpdateTrayMenu);

                while (!cancellationToken.IsCancellationRequested)
                {
                    if (await WaitForRetryOrDelayAsync(TimeSpan.FromSeconds(3), cancellationToken))
                        break;

                    if (cancellationToken.IsCancellationRequested)
                        break;

                    _status = await _greeService.GetStatusAsync(cancellationToken);
                    _isConnected = true;
                    _connectionMessage = string.Empty;
                    await Dispatcher.InvokeAsync(UpdateTrayMenu);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogException("Falha na conexão com o ar-condicionado", ex);
                _isConnected = false;
                _status = null;
                _greeService = null;
                _connectionMessage = ex.Message;
                await Dispatcher.InvokeAsync(UpdateTrayMenu);

                await WaitForRetryOrDelayAsync(retryDelay, cancellationToken);
                retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, 40));
            }
        }
    }

    private async Task<(GreeService Service, GreeStatus Status)> ConnectAsync(CancellationToken cancellationToken)
    {
        GreeSession? cachedSession = null;
        try
        {
            cachedSession = await _sessionStore.LoadAsync(cancellationToken);
        }
        catch (IOException ex)
        {
            LogException("Não foi possível ler a sessão salva", ex);
        }

        var forceSelection = Interlocked.Exchange(ref _forceDeviceSelection, 0) == 1;
        var preferredMac = forceSelection ? null : _config.DeviceMac ?? cachedSession?.Mac;
        var lastIp = _config.LastKnownIp ?? cachedSession?.IpAddress.ToString();
        var devices = await _discoveryService.ScanAsync(lastIp, cancellationToken);

        if (devices.Count == 0)
            throw new IOException("Nenhum aparelho Gree foi encontrado. Confira o Wi-Fi e tente novamente.");

        GreeDeviceInfo? target = null;
        if (!forceSelection && !string.IsNullOrWhiteSpace(preferredMac))
        {
            var normalizedMac = GreeDiscoveryService.NormalizeMac(preferredMac);
            target = devices.FirstOrDefault(device =>
                string.Equals(device.Mac, normalizedMac, StringComparison.OrdinalIgnoreCase));

            if (target is null)
                throw new IOException($"O aparelho salvo ({normalizedMac}) não foi encontrado na rede local.");
        }
        else if (devices.Count == 1)
        {
            target = devices[0];
        }
        else if (_selectionDeclined && !forceSelection)
        {
            throw new IOException("Há vários aparelhos na rede. Use ‘Selecionar aparelho’ no menu do AC Controller.");
        }
        else
        {
            target = await ShowDeviceSelectionAsync(devices);
            if (target is null)
            {
                _selectionDeclined = true;
                throw new IOException("Seleção cancelada. Use ‘Selecionar aparelho’ no menu para escolher uma unidade.");
            }

            _selectionDeclined = false;
        }

        if (cachedSession is not null &&
            string.Equals(cachedSession.Mac, target.Mac, StringComparison.OrdinalIgnoreCase))
        {
            var currentSession = cachedSession with
            {
                IpAddress = target.IpAddress,
                Port = target.Port
            };
            var cachedService = new GreeService(currentSession);

            try
            {
                var cachedStatus = await cachedService.GetStatusAsync(cancellationToken);
                await _sessionStore.SaveAsync(currentSession, cancellationToken);
                SaveDeviceSettings(target);
                return (cachedService, cachedStatus);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogException("A sessão salva não funcionou; negociando uma nova chave", ex);
            }
        }

        var bindingService = new GreeBindingService(_discoveryService, _networkService);
        var session = await bindingService.BindAsync(target, cancellationToken);
        var service = new GreeService(session);
        var status = await service.GetStatusAsync(cancellationToken);

        await _sessionStore.SaveAsync(session, cancellationToken);
        SaveDeviceSettings(new GreeDeviceInfo(
            session.Mac,
            session.IpAddress,
            session.Port,
            session.EncryptionVersion,
            target.Name,
            target.Model));

        return (service, status);
    }

    private async Task<GreeDeviceInfo?> ShowDeviceSelectionAsync(IReadOnlyList<GreeDeviceInfo> devices)
    {
        var operation = Dispatcher.InvokeAsync(() =>
        {
            var window = new DeviceSelectionWindow(devices);
            return window.ShowDialog() == true ? window.SelectedDevice : null;
        });

        return await operation.Task;
    }

    private void SaveDeviceSettings(GreeDeviceInfo device)
    {
        _config = new Config
        {
            DeviceMac = device.Mac,
            LastKnownIp = device.IpAddress.ToString(),
            StartWithWindows = _config.StartWithWindows
        };
        ConfigService.Save(_config);
    }

    private async Task<bool> WaitForRetryOrDelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delayTask = Task.Delay(delay, waitCancellation.Token);
        var retryTask = _reconnectSignal.WaitAsync(waitCancellation.Token);
        var completed = await Task.WhenAny(delayTask, retryTask);
        waitCancellation.Cancel();

        try
        {
            await completed;
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        return completed == retryTask && !cancellationToken.IsCancellationRequested;
    }

    private void RequestReconnect(bool chooseDevice)
    {
        if (chooseDevice)
        {
            _selectionDeclined = false;
            Interlocked.Exchange(ref _forceDeviceSelection, 1);
        }

        if (_reconnectSignal.CurrentCount == 0)
            _reconnectSignal.Release();
    }

    private void UpdateTrayMenu()
    {
        if (_trayIcon is null || (_trayIcon.ContextMenu?.IsOpen ?? false))
            return;

        if (!_isConnected || _status is null || _greeService is null)
        {
            _trayIcon.ToolTipText = $"AC Controller - {_connectionMessage}";
            var disconnectedMenu = new ContextMenu();
            disconnectedMenu.Items.Add(new MenuItem
            {
                Header = string.IsNullOrWhiteSpace(_connectionMessage) ? "Procurando aparelho…" : _connectionMessage,
                IsEnabled = false
            });
            disconnectedMenu.Items.Add(new Separator());

            var retry = new MenuItem { Header = "Tentar novamente" };
            retry.Click += (_, _) => RequestReconnect(chooseDevice: false);
            disconnectedMenu.Items.Add(retry);

            var choose = new MenuItem { Header = "Selecionar aparelho…" };
            choose.Click += (_, _) => RequestReconnect(chooseDevice: true);
            disconnectedMenu.Items.Add(choose);

            disconnectedMenu.Items.Add(new Separator());
            var exitDisconnected = new MenuItem { Header = "Sair" };
            exitDisconnected.Click += (_, _) => Shutdown();
            disconnectedMenu.Items.Add(exitDisconnected);
            _trayIcon.ContextMenu = disconnectedMenu;
            return;
        }

        _trayIcon.ToolTipText = BuildTooltip();
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem
        {
            Header = _status.IsPoweredOn ? "Ar-condicionado: Ligado" : "Ar-condicionado: Desligado",
            IsEnabled = false
        });
        menu.Items.Add(new MenuItem
        {
            Header = $"Aparelho: {_greeService.Session.Mac} ({_greeService.Session.IpAddress})",
            IsEnabled = false
        });

        if (_status.IsPoweredOn)
        {
            menu.Items.Add(new MenuItem { Header = $"Modo: {GetModeName()}", IsEnabled = false });
            menu.Items.Add(new MenuItem { Header = $"Temperatura: {_status.SetTem}°C", IsEnabled = false });
            menu.Items.Add(new MenuItem { Header = $"Velocidade: {GetSpeedName()}", IsEnabled = false });
            menu.Items.Add(new MenuItem { Header = $"Direção: {GetFinDirectionName()}", IsEnabled = false });
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
        var switchDevice = new MenuItem { Header = "Selecionar outro aparelho…" };
        switchDevice.Click += (_, _) => RequestReconnect(chooseDevice: true);
        menu.Items.Add(switchDevice);

        menu.Items.Add(new Separator());
        var exit = new MenuItem { Header = "Sair" };
        exit.Click += (_, _) => Shutdown();
        menu.Items.Add(exit);
        _trayIcon.ContextMenu = menu;
    }

    private MenuItem CreatePowerMenu()
    {
        var item = new MenuItem { Header = _status!.IsPoweredOn ? "Desligar" : "Ligar" };
        item.Click += async (_, _) =>
        {
            try
            {
                if (_status!.IsPoweredOn) await _greeService!.TurnOffAsync();
                else await _greeService!.TurnOnAsync();
                await RefreshStatusAsync();
            }
            catch (Exception ex) { LogException("Falha ao alterar energia", ex); }
        };
        return item;
    }

    private MenuItem CreateModeMenu()
    {
        var menu = new MenuItem { Header = "Modo" };
        var heat = new MenuItem { Header = "Quente", IsChecked = _status!.Mod == 4 };
        heat.Click += async (_, _) =>
        {
            try { if (_status!.Mod != 4) await _greeService!.SetModeAsync(4); await RefreshStatusAsync(); }
            catch (Exception ex) { LogException("Falha ao selecionar modo quente", ex); }
        };

        var cool = new MenuItem { Header = "Frio", IsChecked = _status.Mod == 1 };
        cool.Click += async (_, _) =>
        {
            try { if (_status!.Mod != 1) await _greeService!.SetModeAsync(1); await RefreshStatusAsync(); }
            catch (Exception ex) { LogException("Falha ao selecionar modo frio", ex); }
        };

        menu.Items.Add(heat);
        menu.Items.Add(cool);
        return menu;
    }

    private MenuItem CreateTemperatureMenu()
    {
        var menu = new MenuItem { Header = "Temperatura" };
        for (var temperature = 16; temperature <= 30; temperature++)
        {
            var selectedTemp = temperature;
            var item = new MenuItem { Header = $"{temperature} °C", IsChecked = _status!.SetTem == temperature };
            item.Click += async (_, _) =>
            {
                try { await _greeService!.SetTemperatureAsync(selectedTemp); await RefreshStatusAsync(); }
                catch (Exception ex) { LogException("Falha ao alterar temperatura", ex); }
            };
            menu.Items.Add(item);
        }
        return menu;
    }

    private MenuItem CreateWindSpeedMenu()
    {
        var menu = new MenuItem { Header = "Velocidade" };
        var speeds = new Dictionary<int, string>
        {
            [1] = "Baixo", [2] = "Médio-baixo", [3] = "Médio",
            [4] = "Médio-alto", [5] = "Alto", [6] = "Forte"
        };

        foreach (var speed in speeds)
        {
            var selectedSpeed = speed.Key;
            var item = new MenuItem
            {
                Header = speed.Value,
                IsChecked = selectedSpeed == 6 ? _status!.Tur == 1 : _status!.Tur == 0 && _status.WdSpd == selectedSpeed
            };
            item.Click += async (_, _) =>
            {
                try { await _greeService!.SetWindSpeedAsync(selectedSpeed); await RefreshStatusAsync(); }
                catch (Exception ex) { LogException("Falha ao alterar velocidade", ex); }
            };
            menu.Items.Add(item);
        }
        return menu;
    }

    private MenuItem CreateFinDirectionMenu()
    {
        var menu = new MenuItem { Header = "Direção das aletas" };
        var directions = new Dictionary<int, string>
        {
            [2] = "Direção 1", [3] = "Direção 2", [4] = "Direção 3",
            [5] = "Direção 4", [6] = "Direção 5", [1] = "Ficar mudando de direção"
        };

        foreach (var direction in directions)
        {
            var selectedDirection = direction.Key;
            var item = new MenuItem { Header = direction.Value, IsChecked = _status!.SwUpDn == selectedDirection };
            item.Click += async (_, _) =>
            {
                try { await _greeService!.SetFinDirectionAsync(selectedDirection); await RefreshStatusAsync(); }
                catch (Exception ex) { LogException("Falha ao alterar direção das aletas", ex); }
            };
            menu.Items.Add(item);
        }
        return menu;
    }

    private string GetModeName() => _status!.Mod switch
    {
        0 => "Auto", 1 => "Frio", 2 => "Desumidificar", 3 => "Ventilar", 4 => "Quente", _ => "Desconhecido"
    };

    private string GetSpeedName()
    {
        if (_status!.Tur == 1) return "Forte";
        return _status.WdSpd switch
        {
            1 => "Baixo", 2 => "Médio-baixo", 3 => "Médio", 4 => "Médio-alto", 5 => "Alto", _ => "Desconhecido"
        };
    }

    private string GetFinDirectionName() => _status!.SwUpDn switch
    {
        1 => "Ficar mudando de direção", 2 => "Direção 1", 3 => "Direção 2",
        4 => "Direção 3", 5 => "Direção 4", 6 => "Direção 5", _ => "Desconhecida"
    };

    private string BuildTooltip()
    {
        if (_status is null) return "AC Controller";
        if (!_status.IsPoweredOn) return "AC Controller\nDesligado";
        return $"AC Controller\nLigado\nModo: {GetModeName()}\n" +
               $"Temperatura: {_status.SetTem}°C\nVelocidade: {GetSpeedName()}\n" +
               $"Direção: {GetFinDirectionName()}";
    }

    private async Task RefreshStatusAsync()
    {
        try
        {
            _status = await _greeService!.GetStatusAsync(_shutdown.Token);
            _isConnected = true;
            await Dispatcher.InvokeAsync(UpdateTrayMenu);
        }
        catch (Exception ex)
        {
            LogException("Falha ao atualizar o status", ex);
            RequestReconnect(chooseDevice: false);
        }
    }

    private static void LogException(string context, Exception exception)
    {
        try
        {
            Directory.CreateDirectory(ConfigService.DataDirectory);
            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} {context}: {exception.GetType().Name}: {exception.Message}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(ConfigService.DataDirectory, "ac-controller.log"), line);
        }
        catch
        {
            // Logging is best effort and must never stop the tray application.
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shutdown.Cancel();
        _trayIcon?.Dispose();
        _trayIconImage?.Dispose();
        _iconStream?.Dispose();
        base.OnExit(e);
    }
}
