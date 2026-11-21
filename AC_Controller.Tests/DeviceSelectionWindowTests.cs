using System.Net;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AC_Controller.Models;

namespace AC_Controller.Tests;

// The modal probes temporarily enter WPF's native window message loop.
[CollectionDefinition(nameof(DeviceDialogCollection), DisableParallelization = true)]
public sealed class DeviceDialogCollection;

[Collection(nameof(DeviceDialogCollection))]
public sealed class DeviceSelectionWindowTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task SelectorDistinguishesConfirmationCancellationAndNoResults(bool confirm, bool empty)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                CheckDialog(confirm, empty);
                done.SetResult();
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(12));
    }

    private static void CheckDialog(bool confirm, bool empty)
    {
        var unit = new GreeDeviceInfo("001122334455", IPAddress.Parse("192.0.2.1"), 7000,
            GreeEncryptionVersion.Gcm, "Sala", "Modelo A");
        var second = unit with { Mac = "AABBCCDDEEFF", IpAddress = IPAddress.Parse("192.0.2.2"), Name = "Quarto", Model = "Modelo B" };
        GreeDeviceInfo[] devices = empty ? [] : [unit, second];
        var window = new DeviceSelectionWindow(devices) { ShowInTaskbar = false, ShowActivated = false, Opacity = 0, Width = 390 };
        Exception? error = null;
        var watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        watchdog.Tick += (_, _) =>
        {
            error = new TimeoutException("O seletor não respondeu à ação.");
            window.Close();
        };

        window.ContentRendered += (_, _) =>
        {
            try
            {
                var list = (ListBox)window.FindName("DeviceList");
                var noResults = (Border)window.FindName("EmptyState");
                var connect = (Button)window.FindName("ConnectButton");
                var cancel = (Button)window.FindName("CancelButton");
                Assert.True(window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip);
                Assert.Equal(empty, noResults.IsVisible);
                Assert.Equal(!empty, connect.IsEnabled);
                Assert.True(connect.IsDefault);
                Assert.True(cancel.IsCancel);
                Assert.Equal(empty ? -1 : 0, list.SelectedIndex);
                Assert.Null(window.SelectedDevice);
                if (confirm)
                {
                    list.SelectedIndex = 1;
                    var card = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(1);
                    var label = AutomationProperties.GetName(card);
                    Assert.Contains("Quarto", label);
                    Assert.Contains("Modelo B", label);
                    Assert.Contains("192.0.2.2", label);
                    Assert.Contains("AA:BB:CC:DD:EE:FF", label);
                    list.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Enter)
                        { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                }
                else
                {
                    var peer = new ButtonAutomationPeer(cancel);
                    ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
                }
            }
            catch (Exception ex)
            {
                error = ex;
                window.Close();
            }
        };
        watchdog.Start();
        bool? result;
        try { result = window.ShowDialog(); }
        finally { watchdog.Stop(); }
        if (error is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        Assert.Equal(confirm, result);
        if (confirm)
            Assert.Same(second, window.SelectedDevice);
        else
            Assert.Null(window.SelectedDevice);
    }
}
