using System.Windows;
using System.Windows.Controls;
using AC_Controller.Models;

namespace AC_Controller;

public sealed class DeviceSelectionWindow : Window
{
    private readonly ComboBox _devices;

    public GreeDeviceInfo? SelectedDevice { get; private set; }

    public DeviceSelectionWindow(IReadOnlyList<GreeDeviceInfo> devices)
    {
        Title = "Selecionar ar-condicionado";
        Width = 650;
        Height = 205;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _devices = new ComboBox
        {
            ItemsSource = devices,
            DisplayMemberPath = nameof(GreeDeviceInfo.DisplayName),
            SelectedIndex = devices.Count > 0 ? 0 : -1,
            Margin = new Thickness(0, 8, 0, 14),
            MinHeight = 30
        };

        var title = new TextBlock
        {
            Text = "Mais de um aparelho Gree foi encontrado. Escolha qual o AC Controller deve controlar.",
            TextWrapping = TextWrapping.Wrap
        };

        var connect = new Button
        {
            Content = "Conectar",
            Width = 110,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Right,
            IsDefault = true
        };
        connect.Click += (_, _) =>
        {
            SelectedDevice = _devices.SelectedItem as GreeDeviceInfo;
            if (SelectedDevice is not null)
                DialogResult = true;
        };

        var content = new StackPanel { Margin = new Thickness(18) };
        content.Children.Add(title);
        content.Children.Add(_devices);
        content.Children.Add(connect);
        Content = content;
    }
}
