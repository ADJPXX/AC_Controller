using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Threading;
using AC_Controller.Models;

namespace AC_Controller.Tests;

[Collection(nameof(DeviceDialogCollection))]
public sealed class ControlWindowTests
{
    [Fact]
    public Task PowerAndTemperatureButtonsSendTheirDisplayedAction() => WithWindow((window, calls) =>
    {
        window.Render(Online());
        Assert.Equal("Desligar", AutomationProperties.GetName(window.HeroAction));
        Click(window.HeroAction);
        Click(window.LowerButton);
        Click(window.RaiseButton);
        window.Render(Online(power: false));
        Assert.Equal("Ligar", AutomationProperties.GetName(window.HeroAction));
        Click(window.HeroAction);

        Assert.Equal(new PanelAction[]
        {
            new(PanelCommand.Power, 0), new(PanelCommand.Temperature, 23),
            new(PanelCommand.Temperature, 25), new(PanelCommand.Power, 1)
        }, calls.Commands);
    });

    [Fact]
    public Task ModeLabelsChooseEachProtocolValue() => WithWindow((window, calls) =>
    {
        string[] modes = ["Auto", "Frio", "Seco", "Ventilar", "Quente"];
        for (var value = 0; value < modes.Length; value++)
        {
            window.Render(Online(mode: (value + 1) % modes.Length));
            var button = Mode(window, modes[value]);
            Assert.False(button.IsChecked);
            var peer = new RadioButtonAutomationPeer(button);
            ((ISelectionItemProvider)peer.GetPattern(PatternInterface.SelectionItem)).Select();
            Assert.True(button.IsChecked);
        }
        Assert.Equal(Enumerable.Range(0, 5).Select(value => new PanelAction(PanelCommand.Mode, value)), calls.Commands);
    });

    [Fact]
    public Task FanAndVaneLabelsChooseEachProtocolValue() => WithWindow((window, calls) =>
    {
        window.Render(Online(fan: 0, swing: 0));
        string[] speeds = ["Baixa", "Média-baixa", "Média", "Média-alta", "Alta", "Turbo"];
        string[] directions = ["Oscilar", "Posição 1", "Posição 2", "Posição 3", "Posição 4", "Posição 5"];
        foreach (var speed in speeds) Select(window.FanField, speed);
        foreach (var direction in directions) Select(window.SwingField, direction);
        Select(window.FanField, "Automática");
        Select(window.SwingField, "Padrão do aparelho");

        var expected = Enumerable.Range(1, 6).Select(value => new PanelAction(PanelCommand.Fan, value))
            .Concat(Enumerable.Range(1, 6).Select(value => new PanelAction(PanelCommand.Swing, value)));
        Assert.Equal(expected, calls.Commands);
    });

    [Theory]
    [InlineData(16, false, true, 17)]
    [InlineData(30, true, false, 29)]
    public Task EndpointDisablesOnlyTheOutwardAdjustment(int temperature, bool lower, bool raise, int expected) =>
        WithWindow((window, calls) =>
        {
            window.Render(Online(temperature: temperature));
            Assert.Equal(lower, window.LowerButton.IsEnabled);
            Assert.Equal(raise, window.RaiseButton.IsEnabled);
            Assert.Throws<ElementNotEnabledException>(() => Click(lower ? window.RaiseButton : window.LowerButton));
            Click(lower ? window.LowerButton : window.RaiseButton);
            Assert.Equal(new PanelAction(PanelCommand.Temperature, expected), Assert.Single(calls.Commands));
        });

    [Fact]
    public Task BusyPoweredOffAndOfflineStatesBlockClimateChanges() => WithWindow((window, calls) =>
    {
        foreach (var state in new[] { Online(busy: true), Online(power: false), Offline(searching: false) })
        {
            window.Render(state);
            Assert.All(window.Modes.Children.OfType<RadioButton>(), button => Assert.False(button.IsEnabled));
            Assert.False(window.FanField.IsEnabled);
            Assert.False(window.SwingField.IsEnabled);
            Assert.Throws<ElementNotEnabledException>(() => Click(window.LowerButton));
            Assert.Throws<ElementNotEnabledException>(() => Click(window.RaiseButton));
            if (state.Busy)
                Assert.Throws<ElementNotEnabledException>(() => Click(window.HeroAction));
            Select(window.FanField, "Turbo");
            Select(window.SwingField, "Posição 5");
            Mode(window, "Quente").IsChecked = true;
        }
        Assert.Empty(calls.Commands);
    });

    [Fact]
    public Task SelectionAndRetryUseSeparateCallbacks() => WithWindow((window, calls) =>
    {
        window.Render(Offline(searching: true));
        Assert.Throws<ElementNotEnabledException>(() => Click(window.HeroAction));
        Click(window.ChooseButton);
        Assert.Equal((1, 0), (calls.Selections, calls.Retries));
        window.Render(Offline(searching: false));
        Click(window.HeroAction);
        Assert.Equal((1, 1), (calls.Selections, calls.Retries));
        window.Render(Online());
        Click(window.ChooseButton);
        Assert.Equal((2, 1), (calls.Selections, calls.Retries));
        Assert.Empty(calls.Commands);
    });

    [Fact]
    public Task PollingRefreshesControlsAndClearsStaleValuesWithoutCommands() => WithWindow((window, calls) =>
    {
        window.Render(Online(mode: 3, temperature: 19, fan: 4, swing: 3));
        Assert.True(Mode(window, "Ventilar").IsChecked);
        Assert.Contains("19", AutomationProperties.GetName(window.Dial));
        Assert.Equal("Média-alta", Label(window.FanField, window.FanField.SelectedItem));
        Assert.Equal("Posição 2", Label(window.SwingField, window.SwingField.SelectedItem));

        window.Render(Online(mode: 4, temperature: 27, fan: 1, turbo: 1, swing: 1));
        Assert.True(Mode(window, "Quente").IsChecked);
        Assert.False(Mode(window, "Ventilar").IsChecked);
        Assert.Equal("Turbo", Label(window.FanField, window.FanField.SelectedItem));
        Assert.Equal("Oscilar", Label(window.SwingField, window.SwingField.SelectedItem));
        Assert.Contains("27", AutomationProperties.GetName(window.Dial));

        window.Render(Offline(searching: false));
        Assert.All(window.Modes.Children.OfType<RadioButton>(), button => Assert.False(button.IsChecked));
        Assert.Null(window.FanField.SelectedItem);
        Assert.Null(window.SwingField.SelectedItem);
        Assert.Contains("Sem conexão", AutomationProperties.GetName(window.Dial));
        Assert.Equal(Visibility.Visible, window.Notice.Visibility);
        Assert.Equal("Sem resposta do aparelho", window.NoticeText.Text);
        Assert.Empty(calls.Commands);
    });

    [Fact]
    public Task ResizingStacksCardsAndRestoresTheWideLayout() => WithWindow((window, calls) =>
    {
        window.Width = 1024;
        window.Height = 640;
        window.Show();
        Layout(window);
        AssertWide(window);

        window.Width = 390;
        window.Height = 440;
        Layout(window);
        var temp = BoundsInControls(window.TempCard, window);
        var options = BoundsInControls(window.OptionsCard, window);
        Assert.True(options.Top >= temp.Bottom);
        Assert.Equal(temp.Left, options.Left, 1);
        Assert.Equal(temp.Width, options.Width, 1);
        Assert.Equal(Visibility.Collapsed, window.ChooseLabel.Visibility);
        Assert.True(window.Scroller.ScrollableHeight > 0);
        Assert.True(window.ChooseButton.IsEnabled);
        Assert.Equal(0.0, window.Scroller.ScrollableWidth);

        window.Width = 1024;
        Layout(window);
        AssertWide(window);
        Assert.Empty(calls.Commands);
    });

    private static void AssertWide(ControlWindow window)
    {
        var temp = BoundsInControls(window.TempCard, window);
        var options = BoundsInControls(window.OptionsCard, window);
        Assert.True(temp.Width > 0 && options.Width > 0);
        Assert.True(options.Left >= temp.Right);
        Assert.Equal(temp.Top, options.Top, 1);
        Assert.Equal(Visibility.Visible, window.ChooseLabel.Visibility);
    }

    [Fact]
    public Task NativeFanDropdownOpensAndOnlyOffersSupportedWrites() => WithWindow((window, calls) =>
    {
        window.Render(Online(fan: 0));
        window.Show();
        Layout(window);
        var field = window.FanField;
        var peer = new ComboBoxAutomationPeer(field);
        var expand = (IExpandCollapseProvider)peer.GetPattern(PatternInterface.ExpandCollapse);
        expand.Expand();
        Layout(window);
        Assert.True(field.IsDropDownOpen);
        var reportedOnly = Assert.IsType<ComboBoxItem>(field.ItemContainerGenerator.ContainerFromIndex(0));
        var turbo = Assert.IsType<ComboBoxItem>(field.ItemContainerGenerator.ContainerFromIndex(6));
        Assert.False(reportedOnly.IsEnabled);
        Assert.True(turbo.IsEnabled);
        Assert.True(turbo.ActualWidth > 0);
        Assert.True(turbo.ActualHeight > 0);
        Assert.Empty(calls.Commands);
        turbo.IsSelected = true;
        Assert.Equal(new PanelAction(PanelCommand.Fan, 6), Assert.Single(calls.Commands));
        expand.Collapse();
    });

    [Fact]
    public Task ConnectionProgressAndCommandFailuresAreShownSeparately() => WithWindow((window, calls) =>
    {
        var connecting = Offline(searching: true) with { ConnectionMessage = "Conectando ao aparelho…" };
        window.Render(connecting);
        Assert.Equal("Conectando ao aparelho…", window.DeviceDetail.Text);
        Assert.Equal(Visibility.Collapsed, window.Notice.Visibility);
        window.Render(connecting with { CommandFailure = "O comando expirou." });
        Assert.Equal(Visibility.Visible, window.Notice.Visibility);
        Assert.Equal("O comando expirou.", window.NoticeText.Text);
        Assert.Empty(calls.Commands);
    });

    private static Rect BoundsInControls(FrameworkElement card, ControlWindow window) =>
        card.TransformToVisual(window.Controls).TransformBounds(new Rect(card.RenderSize));

    private static void Layout(ControlWindow window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }

    private static RadioButton Mode(ControlWindow window, string label) => window.Modes.Children.OfType<RadioButton>()
        .Single(button => AutomationProperties.GetName(button) == $"Modo {label}");

    private static string? Label(ComboBox field, object option) =>
        TypeDescriptor.GetProperties(option)[field.DisplayMemberPath]?.GetValue(option) as string;

    private static void Select(ComboBox field, string label) =>
        field.SelectedItem = field.Items.Cast<object>().Single(item => Label(field, item) == label);

    private static void Click(Button button)
    {
        var peer = new ButtonAutomationPeer(button);
        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
        button.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static PanelState Online(bool power = true, bool busy = false, int temperature = 24,
        int mode = 1, int fan = 2, int turbo = 0, int swing = 1) =>
        new(new GreeStatus { Pow = power ? 1 : 0, SetTem = temperature, Mod = mode,
            WdSpd = fan, Tur = turbo, SwUpDn = swing }, "Sala", "192.0.2.1", string.Empty, false, busy);

    private static PanelState Offline(bool searching) =>
        new(null, null, null, searching ? string.Empty : "Sem resposta do aparelho", searching, false);

    private sealed class CallLog
    {
        public List<PanelAction> Commands { get; } = [];
        public int Selections { get; set; }
        public int Retries { get; set; }
    }

    private static Task WithWindow(Action<ControlWindow, CallLog> check)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var calls = new CallLog();
                var window = new ControlWindow(action =>
                {
                    calls.Commands.Add(action);
                    return Task.CompletedTask;
                }, () => calls.Selections++, () => calls.Retries++)
                {
                    ShowInTaskbar = false, ShowActivated = false, Opacity = 0
                };
                try { check(window, calls); }
                finally { window.Close(); }
                completed.SetResult();
            }
            catch (Exception error)
            {
                completed.SetException(error);
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completed.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }
}
