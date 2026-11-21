using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AC_Controller;

public partial class ControlWindow : Window
{
    private readonly Func<PanelAction, Task> _execute;
    private readonly Action _choose;
    private readonly Action _reconnect;
    private readonly Dictionary<int, RadioButton> _modeButtons = [];
    private PanelState _state = new(null, null, null, string.Empty, true, false);
    private bool _rendering;
    private DateTime? _lastRead;

    private static readonly Geometry PowerIcon = Geometry.Parse("M12,2 V12 M7.5,5.5 A8,8 0 1 0 16.5,5.5");
    private static readonly Geometry SearchIcon = Geometry.Parse("M15,15 L21,21 M17,10 A7,7 0 1 1 3,10 A7,7 0 1 1 17,10");
    private sealed record Option(int Value, string Label, bool Selectable = true);
    private static readonly Option[] FanOptions =
    [
        new(0, "Automática", false), new(1, "Baixa"), new(2, "Média-baixa"), new(3, "Média"),
        new(4, "Média-alta"), new(5, "Alta"), new(6, "Turbo")
    ];
    private static readonly Option[] SwingOptions =
    [
        new(0, "Padrão do aparelho", false), new(1, "Oscilar"), new(2, "Posição 1"),
        new(3, "Posição 2"), new(4, "Posição 3"), new(5, "Posição 4"), new(6, "Posição 5")
    ];

    internal ControlWindow(Func<PanelAction, Task> execute, Action chooseDevice, Action reconnect)
    {
        _execute = execute;
        _choose = chooseDevice;
        _reconnect = reconnect;
        InitializeComponent();
        FanField.ItemsSource = FanOptions;
        SwingField.ItemsSource = SwingOptions;
        AddMode(0, "Auto", "M4,10 A8,8 0 0 1 18,6 L20,8 M20,3 V8 H15 M20,14 A8,8 0 0 1 6,18 L4,16 M4,21 V16 H9");
        AddMode(1, "Frio", "M12,2 V22 M3.4,7 L20.6,17 M3.4,17 L20.6,7 M9,4 L12,7 15,4 M9,20 L12,17 15,20 M4,10 L7,9 7,5 M17,19 V15 L21,14 M4,14 L7,15 7,19 M17,5 V9 L21,10");
        AddMode(2, "Seco", "M12,2 C10,6 5,11 5,15 A7,7 0 0 0 19,15 C19,11 14,6 12,2 Z M8,15 Q8,18 11,19");
        AddMode(3, "Ventilar", "M2,8 H12 C17,8 17,2 13,2 C11,2 10,3 10,4 M2,12 H19 C24,12 24,6 21,6 M4,16 H14 C19,16 19,22 15,22");
        AddMode(4, "Quente", "M17,12 A5,5 0 1 1 7,12 A5,5 0 1 1 17,12 M12,1 V4 M12,20 V23 M1,12 H4 M20,12 H23 M4,4 L6,6 M18,18 L20,20 M4,20 L6,18 M18,6 L20,4");
        Scroller.SizeChanged += (_, e) => UpdateLayoutForWidth(e.NewSize.Width);
        Width = Math.Min(Width, SystemParameters.WorkArea.Width);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);
        UpdateLayoutForWidth(Width);
        Render(_state);
    }

    private void AddMode(int value, string label, string geometry)
    {
        var icon = new Path
        {
            Data = Geometry.Parse(geometry), StrokeThickness = 1.6,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round, Stretch = Stretch.Uniform, Width = 21, Height = 21,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        icon.SetBinding(Shape.StrokeProperty, new Binding(nameof(Foreground))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(RadioButton), 1)
        });
        var content = new StackPanel();
        content.Children.Add(icon);
        content.Children.Add(new TextBlock { Text = label, FontSize = 12, Margin = new Thickness(0, 4, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center });
        var button = new RadioButton
        {
            GroupName = "ClimateMode", Style = (Style)FindResource("ModeChoice"), Content = content,
            Margin = new Thickness(2, 0, 2, 0)
        };
        AutomationProperties.SetName(button, $"Modo {label}");
        button.Checked += async (_, _) =>
        {
            if (!_rendering && _state.Status?.Mod != value)
                await SendAsync(new PanelAction(PanelCommand.Mode, value));
        };
        _modeButtons.Add(value, button);
        Modes.Children.Add(button);
    }

    private void UpdateLayoutForWidth(double width)
    {
        var stacked = width < 790;
        var narrow = width < 550;
        var inset = narrow ? 14 : 28;
        Header.Margin = new Thickness(inset, 0, inset, 0);
        ContentArea.Margin = new Thickness(inset, narrow ? 14 : 22, inset, 16);
        Header.Height = narrow ? 56 : 64;
        ChooseLabel.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
        ChooseButton.Padding = new Thickness(narrow ? 9 : 14, 8, narrow ? 9 : 14, 8);
        Brand.FontSize = narrow ? 17 : 21;
        Hero.Height = narrow ? 172 : 196;
        ShadeMiddle.Offset = narrow ? .55 : .32;
        ShadeEdge.Offset = narrow ? .83 : .59;
        DeviceTitle.FontSize = narrow ? 28 : 36;
        HeroText.Margin = new Thickness(narrow ? 16 : 26, 10, narrow ? 16 : 26, 10);
        var heroWidth = Math.Min(width - inset * 2, ContentArea.MaxWidth);
        HeroText.MaxWidth = Math.Max(170, heroWidth * (narrow ? .69 : .48));
        TempColumn.Width = new GridLength(stacked ? 1 : 36, GridUnitType.Star);
        ControlGap.Width = new GridLength(stacked ? 0 : 16);
        OptionsColumn.Width = stacked ? new GridLength(0) : new GridLength(64, GridUnitType.Star);
        Grid.SetColumn(OptionsCard, stacked ? 0 : 2);
        Grid.SetRow(OptionsCard, stacked ? 1 : 0);
        OptionsCard.Margin = new Thickness(0, stacked ? 12 : 0, 0, 0);
        TempCard.Padding = OptionsCard.Padding = narrow ? new Thickness(14, 15, 14, 15) : new Thickness(22, 18, 22, 18);
        FanField.Width = SwingField.Width = narrow ? 132 : 156;
    }

    internal void Render(PanelState state)
    {
        if (state.Status is not null && !ReferenceEquals(state.Status, _state.Status)) _lastRead = DateTime.Now;
        if (state.Status is null) _lastRead = null;
        _state = state;
        var status = state.Status;
        var connected = status is not null;
        var powered = status?.IsPoweredOn == true;
        var adjustable = powered && !state.Busy;
        _rendering = true;
        try
        {
            ConnectionLabel.Text = connected ? "Conectado" : state.Searching ? "Buscando" : "Sem conexão";
            ConnectionPill.Background = connected ? (Brush)FindResource("Mint") : Brushes.WhiteSmoke;
            ConnectionLabel.Foreground = connected ? (Brush)FindResource("Teal") : (Brush)FindResource("SecondaryInk");
            StatusDot.Fill = ConnectionLabel.Foreground;
            HeroKicker.Text = connected ? "APARELHO SELECIONADO" : "REDE LOCAL";
            DeviceTitle.Text = !string.IsNullOrWhiteSpace(state.DeviceName) ? state.DeviceName : "Ar-condicionado";
            DeviceTitle.ToolTip = DeviceTitle.Text;
            DeviceDetail.Text = connected
                ? string.Join("  ·  ", new[] { powered ? "Ligado" : "Desligado", state.DeviceAddress }
                    .Where(value => !string.IsNullOrWhiteSpace(value)))
                : state.Searching && !string.IsNullOrWhiteSpace(state.ConnectionMessage) ? state.ConnectionMessage
                : state.Searching ? "Procurando aparelho na rede…" : "Aguardando conexão com o aparelho";
            HeroActionText.Text = connected ? powered ? "Desligar" : "Ligar" : state.Searching ? "Buscando…" : "Buscar novamente";
            HeroIcon.Data = connected ? PowerIcon : SearchIcon;
            HeroAction.IsEnabled = connected ? !state.Busy : !state.Searching;
            AutomationProperties.SetName(HeroAction, HeroActionText.Text);
            var message = string.Join(" ", new[] { state.CommandFailure, !connected && !state.Searching ? state.ConnectionMessage : null }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            bool notice = message.Length != 0;
            Notice.Visibility = notice ? Visibility.Visible : Visibility.Collapsed;
            if (notice && NoticeText.Text != message) NoticeText.Text = message;
            Dial.SetReading(status?.SetTem, powered);
            LowerButton.IsEnabled = adjustable && status!.SetTem > 16;
            RaiseButton.IsEnabled = adjustable && status!.SetTem < 30;
            foreach (var (mode, button) in _modeButtons)
            {
                button.IsChecked = status?.Mod == mode;
                button.IsEnabled = adjustable;
            }
            FanField.IsEnabled = SwingField.IsEnabled = adjustable;
            FanField.SelectedItem = status is null ? null : FanOptions.FirstOrDefault(x => x.Value == (status.Tur == 1 ? 6 : status.WdSpd));
            SwingField.SelectedItem = status is null ? null : SwingOptions.FirstOrDefault(x => x.Value == status.SwUpDn);
            Freshness.Text = state.Busy ? "Confirmando o ajuste com o aparelho…"
                : _lastRead is { } last ? $"Última leitura às {last:HH:mm:ss}  ·  Atualização automática"
                : "Os ajustes ficam disponíveis quando o aparelho está conectado e ligado.";
        }
        finally { _rendering = false; }
    }

    private async Task SendAsync(PanelAction action)
    {
        if (_rendering || !action.IsAvailable(_state.Status, _state.Busy)) return;
        await _execute(action);
    }

    private void ChooseClick(object sender, RoutedEventArgs e) => _choose();

    private async void HeroClick(object sender, RoutedEventArgs e)
    {
        if (_state.Status is { } status)
            await SendAsync(new PanelAction(PanelCommand.Power, status.IsPoweredOn ? 0 : 1));
        else if (!_state.Searching)
            _reconnect();
    }

    private async void LowerClick(object sender, RoutedEventArgs e) => await AdjustTemperatureAsync(-1);
    private async void RaiseClick(object sender, RoutedEventArgs e) => await AdjustTemperatureAsync(1);

    private Task AdjustTemperatureAsync(int step) => _state.Status is { } status
        ? SendAsync(new PanelAction(PanelCommand.Temperature, Math.Clamp(status.SetTem + step, 16, 30)))
        : Task.CompletedTask;

    private async void FanChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FanField.SelectedItem is Option { Selectable: true } option)
            await SendAsync(new PanelAction(PanelCommand.Fan, option.Value));
    }

    private async void SwingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SwingField.SelectedItem is Option { Selectable: true } option)
            await SendAsync(new PanelAction(PanelCommand.Swing, option.Value));
    }
}
