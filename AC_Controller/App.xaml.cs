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
    private const string SearchingMessage = "Procurando o ar-condicionado na rede local…";
    private GreeService? _greeService;
    private GreeStatus? _status;
    private TaskbarIcon? _trayIcon;
    private Stream? _iconStream;
    private Icon? _trayIconImage;
    private string _connectionMessage = SearchingMessage;
    private string? _commandFailure;
    private bool _searching = true;
    private int _pendingCommands;
    private GreeDeviceInfo? _device;
    private ControlWindow? _panel;
    private TrayDeviceSession? _displayedSession;
    private readonly GreeDiscoveryService _discoveryService = new();
    private readonly GreeNetworkService _networkService = new();
    private readonly TrayPreferences _preferences;
    private TrayDeviceSession? _operations;
    private Task _connectionLoop = Task.CompletedTask;
    private CancellationTokenSource? _attempt;
    private DeviceSelectionWindow? _selectionWindow;
    private readonly TraySelection _selection = new();
    private bool _stopping;

    public App()
    {
        var sessions = new GreeSessionStore();
        _preferences = new TrayPreferences(
            () => ConfigService.ReadJson(ex => LogException("Aviso ao ler as preferências", ex)),
            new StartupService().TaskVerification, ConfigService.Save,
            sessions.LoadAsync, sessions.SaveAsync, LogException);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _preferences.Initialize();

            _iconStream = typeof(App).Assembly.GetManifestResourceStream("AC_Controller.Resources.accontroller.ico")
                          ?? throw new InvalidOperationException("Não foi possível carregar o ícone do aplicativo.");
            _trayIconImage = new Icon(_iconStream);
            _trayIcon = new TaskbarIcon
            {
                Icon = _trayIconImage,
                ToolTipText = "AC Controller - procurando o aparelho"
            };
            _trayIcon.TrayLeftMouseUp += OnTrayClick;
            OpenPanel();
            UpdateTrayMenu();
            _connectionLoop = SuperviseConnectionAsync();
        }
        catch (Exception ex)
        {
            LogException("Falha ao iniciar", ex);
            MessageBox.Show(ex.Message, "AC Controller", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private async Task SuperviseConnectionAsync()
    {
        try
        {
            await RunConnectionLoopAsync();
        }
        catch (Exception ex)
        {
            _attempt = null;
            LogException("Falha inesperada no ciclo de conexão", ex);
            if (!_stopping)
            {
                MessageBox.Show(ex.Message, "AC Controller", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }
    }

    private async Task RunConnectionLoopAsync()
    {
        var retryDelay = TimeSpan.FromSeconds(4);
        while (!_stopping)
        {
            using var attempt = new CancellationTokenSource();
            using var sessionLifetime = CancellationTokenSource.CreateLinkedTokenSource(attempt.Token);
            _attempt = attempt;
            var failed = false;
            try
            {
                _connectionMessage = SearchingMessage;
                _searching = true;
                RefreshViews();
                var (service, status) = await ConnectAsync(attempt.Token);
                attempt.Token.ThrowIfCancellationRequested();
                _greeService = service;
                if (_device is not null)
                    _device = _device with { IpAddress = service.Session.IpAddress, Port = service.Session.Port };
                var operations = new TrayDeviceSession(service.GetStatusAsync, PublishStatus, sessionLifetime.Token);
                _operations = operations;
                _commandFailure = null;
                _searching = false;
                PublishStatus(status);
                retryDelay = TimeSpan.FromSeconds(4);

                while (true)
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), attempt.Token);
                    await operations.RunAsync();
                }
            }
            catch (OperationCanceledException) when (attempt.IsCancellationRequested)
            {
                // Selection changed or the application is exiting.
            }
            catch (Exception ex)
            {
                failed = true;
                _searching = false;
                _connectionMessage = ex.Message;
                LogException("Falha na conexão com o ar-condicionado", ex);
            }
            finally
            {
                var oldSession = _operations;
                _operations = null;
                _greeService = null;
                _status = null;
                sessionLifetime.Cancel();
                RefreshViews();
                if (oldSession is not null)
                    await oldSession.DrainAsync();
            }

            if (failed && !attempt.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(retryDelay, attempt.Token);
                    retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, 40));
                }
                catch (OperationCanceledException) when (attempt.IsCancellationRequested)
                {
                    // The new request takes precedence over the remaining backoff.
                }
            }
            _attempt = null;
        }
    }

    private void PublishStatus(GreeStatus status)
    {
        _status = status;
        _connectionMessage = string.Empty;
        RefreshViews();
    }

    private async Task<(GreeService Service, GreeStatus Status)> ConnectAsync(CancellationToken cancellationToken)
    {
        var cachedSession = await _preferences.LoadSessionAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var preferredMac = _preferences.Settings.DeviceMac ?? cachedSession?.Mac;
        var lastIp = _preferences.Settings.LastKnownIp ?? cachedSession?.IpAddress.ToString();
        var devices = await _discoveryService.ScanAsync(lastIp, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var target = _selection.Choose(devices, preferredMac, ShowDeviceSelection, cancellationToken);
        _device = target;
        _connectionMessage = "Conectando ao aparelho…";
        RefreshViews();
        if (cachedSession is not null &&
            string.Equals(cachedSession.Mac, target.Mac, StringComparison.OrdinalIgnoreCase))
        {
            var currentSession = cachedSession with
            {
                IpAddress = target.IpAddress,
                Port = target.Port
            };
            var cachedService = new GreeService(currentSession);

            GreeStatus? cachedStatus = null;
            try
            {
                cachedStatus = await cachedService.GetStatusAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogException("A sessão salva não funcionou durante a leitura do status", ex);
            }

            if (cachedStatus is not null)
            {
                await _preferences.RememberAsync(currentSession, cachedSession, cancellationToken);
                return (cachedService, cachedStatus);
            }
        }

        var bindingService = new GreeBindingService(_discoveryService, _networkService);
        var session = await bindingService.BindAsync(target, cancellationToken);
        var service = new GreeService(session);
        var status = await service.GetStatusAsync(cancellationToken);

        await _preferences.RememberAsync(session, cachedSession, cancellationToken);
        return (service, status);
    }

    private GreeDeviceInfo? ShowDeviceSelection(IReadOnlyList<GreeDeviceInfo> devices)
    {
        _selectionWindow = new DeviceSelectionWindow(devices);
        if (_panel is { IsVisible: true } owner)
        {
            _selectionWindow.Owner = owner;
            _selectionWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        try
        {
            return _selectionWindow.ShowDialog() == true ? _selectionWindow.SelectedDevice : null;
        }
        finally
        {
            _selectionWindow = null;
        }
    }

    private void RequestReconnect(bool chooseDevice)
    {
        if (_stopping) return;
        if (chooseDevice)
        {
            _selection.RequestChoice();
            _device = null;
            _commandFailure = null;
        }
        _connectionMessage = chooseDevice ? SearchingMessage : "Reconectando ao aparelho…";
        _searching = true;
        CancelAttempt();
        RefreshViews();
    }

    private void CancelAttempt()
    {
        _attempt?.Cancel();
        if (_selectionWindow?.IsVisible == true)
            _selectionWindow.Close();
    }

    private void OnTrayClick(object sender, RoutedEventArgs e) => OpenPanel();

    private void OpenPanel()
    {
        if (_stopping) return;
        if (_selectionWindow is { IsVisible: true } selector)
        {
            selector.Activate();
            return;
        }
        if (_panel is null)
        {
            ControlWindow? window = null;
            window = new ControlWindow(
                action => ReferenceEquals(window, _panel) ? ExecutePanelActionAsync(action) : Task.CompletedTask,
                () => { if (ReferenceEquals(window, _panel)) RequestReconnect(chooseDevice: true); },
                () => { if (ReferenceEquals(window, _panel)) RequestReconnect(chooseDevice: false); });
            window.Closed += OnPanelClosed;
            _panel = window;
            MainWindow = window;
        }
        RenderPanel();
        if (_panel.WindowState == WindowState.Minimized) _panel.WindowState = WindowState.Normal;
        if (!_panel.IsVisible) _panel.Show();
        _panel.Activate();
    }

    private void OnPanelClosed(object? sender, EventArgs e)
    {
        if (sender is ControlWindow closed) closed.Closed -= OnPanelClosed;
        if (!ReferenceEquals(sender, _panel)) return;
        _panel = null;
        _displayedSession = null;
    }

    private string ConnectionText => _commandFailure is null
        ? _connectionMessage
        : $"{_commandFailure} {_connectionMessage}".TrimEnd();

    private void RenderPanel()
    {
        if (_stopping || _panel is null) return;
        var panel = _panel;
        var session = _operations is { IsCancelled: false } ? _operations : null;
        var name = _device switch
        {
            null => null,
            { } device when !string.IsNullOrWhiteSpace(device.Name) => device.Name,
            { } device when !string.IsNullOrWhiteSpace(device.Model) => device.Model,
            { } device => device.Mac
        };
        // Rendering a selected item can raise WPF change events synchronously.
        _displayedSession = null;
        panel.Render(new PanelState(session is null ? null : _status, name,
            _device?.IpAddress.ToString() ?? _preferences.Settings.LastKnownIp,
            _connectionMessage, _searching, _pendingCommands != 0, _commandFailure));
        if (ReferenceEquals(panel, _panel)) _displayedSession = session;
    }

    private void RefreshViews()
    {
        RenderPanel();
        UpdateTrayMenu();
    }

    private Task ExecutePanelActionAsync(PanelAction action)
    {
        var owner = _displayedSession;
        var service = _greeService;
        if (owner is null || service is null || !action.IsAvailable(_status, _pendingCommands != 0))
            return Task.CompletedTask;

        return ExecuteCommandAsync(owner, token => action.SendAsync(service, token), "Comando do painel");
    }

    private void UpdateTrayMenu()
    {
        if (_stopping || _trayIcon is null) return;
        if (_trayIcon.ContextMenu is { IsOpen: true } openMenu)
        {
            openMenu.Closed -= OnMenuClosed;
            openMenu.Closed += OnMenuClosed;
            return;
        }

        if (_operations is null || _operations.IsCancelled || _status is null || _greeService is null)
        {
            var tooltip = $"AC Controller - {ConnectionText}";
            _trayIcon.ToolTipText = tooltip.Length <= 127 ? tooltip : tooltip[..126] + "…";
            var disconnectedMenu = new ContextMenu();
            AddOpenPanelItem(disconnectedMenu);
            disconnectedMenu.Items.Add(new MenuItem
            {
                Header = string.IsNullOrWhiteSpace(ConnectionText) ? "Procurando aparelho…" : ConnectionText,
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
            exitDisconnected.Click += async (_, _) => await ExitAsync();
            disconnectedMenu.Items.Add(exitDisconnected);
            _trayIcon.ContextMenu = disconnectedMenu;
            return;
        }

        _trayIcon.ToolTipText = BuildTooltip();
        var menu = new ContextMenu();
        AddOpenPanelItem(menu);
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
            menu.Items.Add(new MenuItem { Header = $"Velocidade: {GetSpeedName(_status!)}", IsEnabled = false });
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
        exit.Click += async (_, _) => await ExitAsync();
        menu.Items.Add(exit);
        _trayIcon.ContextMenu = menu;
    }

    private void OnMenuClosed(object sender, RoutedEventArgs e)
    {
        ((ContextMenu)sender).Closed -= OnMenuClosed;
        UpdateTrayMenu();
    }

    private void AddOpenPanelItem(ContextMenu menu)
    {
        var open = new MenuItem { Header = "Abrir painel" };
        open.Click += (_, _) => OpenPanel();
        menu.Items.Add(open);
        menu.Items.Add(new Separator());
    }

    private MenuItem DeviceAction(
        string label, Func<CancellationToken, Task> sendCommand, bool isChecked = false)
    {
        var owner = _operations!;
        var item = new MenuItem { Header = label, IsChecked = isChecked };
        item.Click += async (_, _) => await ExecuteCommandAsync(owner, sendCommand, label);
        return item;
    }

    private async Task ExecuteCommandAsync(
        TrayDeviceSession owner, Func<CancellationToken, Task> sendCommand, string label)
    {
        if (_stopping || !ReferenceEquals(_operations, owner) || owner.IsCancelled) return;
        _pendingCommands++;
        try
        {
            RenderPanel();
            await owner.RunAsync(sendCommand);
        }
        catch (OperationCanceledException) when (owner.IsCancelled)
        {
            // The user changed devices or exited before the action completed.
        }
        catch (Exception ex)
        {
            LogException($"Falha no comando '{label}'", ex);
            if (ReferenceEquals(_operations, owner) && !owner.IsCancelled)
            {
                _commandFailure = $"Falha no comando. {ex.Message}";
                RequestReconnect(chooseDevice: false);
            }
        }
        finally
        {
            _pendingCommands--;
            RenderPanel();
        }
    }

    private MenuItem CreatePowerMenu()
    {
        var service = _greeService!;
        var (label, command) = PowerAction(_status!.IsPoweredOn, service.TurnOnAsync, service.TurnOffAsync);
        return DeviceAction(label, command);
    }

    internal static (string Label, Func<CancellationToken, Task> Command) PowerAction(
        bool isPoweredOn, Func<CancellationToken, Task> turnOn, Func<CancellationToken, Task> turnOff) =>
        isPoweredOn ? ("Desligar", turnOff) : ("Ligar", turnOn);

    private MenuItem CreateModeMenu()
    {
        var service = _greeService!;
        var menu = new MenuItem { Header = "Modo" };
        Task SelectModeAsync(int mode, CancellationToken token) =>
            _status?.Mod == mode ? Task.CompletedTask : service.SetModeAsync(mode, token);

        menu.Items.Add(DeviceAction("Quente", ct => SelectModeAsync(4, ct), _status!.Mod == 4));
        menu.Items.Add(DeviceAction("Frio", ct => SelectModeAsync(1, ct), _status.Mod == 1));
        return menu;
    }

    private MenuItem CreateTemperatureMenu()
    {
        var service = _greeService!;
        var menu = new MenuItem { Header = "Temperatura" };
        for (var temperature = 16; temperature <= 30; temperature++)
        {
            var value = temperature;
            menu.Items.Add(DeviceAction($"{value} °C", ct => service.SetTemperatureAsync(value, ct), _status!.SetTem == value));
        }
        return menu;
    }

    private MenuItem CreateWindSpeedMenu()
    {
        var service = _greeService!;
        var menu = new MenuItem { Header = "Velocidade" };
        string[] labels = ["Baixo", "Médio-baixo", "Médio", "Médio-alto", "Alto", "Forte"];
        for (var i = 0; i < labels.Length; i++)
        {
            var speed = i + 1;
            var selected = speed == 6 ? _status!.Tur == 1 : _status!.Tur == 0 && _status.WdSpd == speed;
            menu.Items.Add(DeviceAction(labels[i], ct => service.SetWindSpeedAsync(speed, ct), selected));
        }
        return menu;
    }

    private MenuItem CreateFinDirectionMenu()
    {
        var service = _greeService!;
        var menu = new MenuItem { Header = "Direção das aletas" };
        foreach (var direction in new[] { 2, 3, 4, 5, 6, 1 })
        {
            var label = direction == 1 ? "Ficar mudando de direção" : $"Direção {direction - 1}";
            menu.Items.Add(DeviceAction(label, ct => service.SetFinDirectionAsync(direction, ct), _status!.SwUpDn == direction));
        }
        return menu;
    }

    private string GetModeName() => _status!.Mod switch
    {
        0 => "Auto", 1 => "Frio", 2 => "Desumidificar", 3 => "Ventilar", 4 => "Quente", _ => "Desconhecido"
    };

    internal static string GetSpeedName(GreeStatus status)
    {
        if (status.Tur == 1) return "Forte";
        return status.WdSpd switch
        {
            0 => "Auto", 1 => "Baixo", 2 => "Médio-baixo", 3 => "Médio", 4 => "Médio-alto", 5 => "Alto", _ => "Desconhecido"
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
               $"Temperatura: {_status.SetTem}°C\nVelocidade: {GetSpeedName(_status!)}\n" +
               $"Direção: {GetFinDirectionName()}";
    }

    private static void LogException(string context, Exception exception) =>
        DiagnosticLog.Write(context, exception);

    private async Task ExitAsync()
    {
        if (_stopping) return;
        _stopping = true;
        CancelAttempt();
        try
        {
            await _connectionLoop;
        }
        catch (Exception ex)
        {
            LogException("Falha ao encerrar o ciclo de conexão", ex);
        }
        finally
        {
            Shutdown();
        }
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        base.OnSessionEnding(e);
        if (!e.Cancel)
        {
            _stopping = true;
            CancelAttempt();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _stopping = true;
        CancelAttempt();
        if (_trayIcon?.ContextMenu is { } menu) menu.Closed -= OnMenuClosed;
        if (_trayIcon is not null) _trayIcon.TrayLeftMouseUp -= OnTrayClick;
        _trayIcon?.Dispose();
        _trayIcon = null;
        _trayIconImage?.Dispose();
        _iconStream?.Dispose();
        base.OnExit(e);
    }
}
