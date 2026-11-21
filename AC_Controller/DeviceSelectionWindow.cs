using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AC_Controller.Models;
using AC_Controller.Services;

namespace AC_Controller;

public sealed partial class DeviceSelectionWindow : Window
{
    public GreeDeviceInfo? SelectedDevice { get; private set; }

    public DeviceSelectionWindow(IReadOnlyList<GreeDeviceInfo> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);
        InitializeComponent();

        DeviceList.ItemsSource = devices.Select(device => new DeviceRow(device)).ToArray();
        DeviceList.SelectedIndex = devices.Count > 0 ? 0 : -1;
        ResultsLabel.Text = devices.Count == 1 ? "1 aparelho encontrado" : $"{devices.Count} aparelhos encontrados";
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        if (DeviceList.SelectedIndex >= 0 &&
            DeviceList.ItemContainerGenerator.ContainerFromIndex(DeviceList.SelectedIndex) is ListBoxItem selected)
            selected.Focus();
        else
            CancelButton.Focus();
    }

    private void ConfirmSelection(object sender, RoutedEventArgs e)
    {
        if (DeviceList.SelectedItem is not DeviceRow selected)
            return;
        SelectedDevice = selected.Device;
        DialogResult = true;
    }

    private void OnDeviceKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None)
            return;
        e.Handled = true;
        ConfirmSelection(sender, e);
    }

    private void OnDeviceDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(DeviceList, source) is ListBoxItem { IsSelected: true })
        {
            e.Handled = true;
            ConfirmSelection(sender, e);
        }
    }

    private sealed class DeviceRow(GreeDeviceInfo device)
    {
        public GreeDeviceInfo Device { get; } = device;
        public string Name { get; } = string.IsNullOrWhiteSpace(device.Name) ? "Aparelho Gree" : device.Name.Trim();
        public string Model { get; } = string.IsNullOrWhiteSpace(device.Model) ? "Modelo não informado" : device.Model.Trim();
        public string Ip => Device.IpAddress.ToString();
        public string Mac { get; } = GreeDiscoveryService.TryNormalizeMac(device.Mac, out var normalized)
            ? string.Join(":", Enumerable.Range(0, 6).Select(index => normalized.Substring(2 * index, 2))).ToUpperInvariant()
            : device.Mac;
        public string AccessibleName => $"{Name}, {Model}, IP {Ip}, MAC {Mac}";
    }
}
