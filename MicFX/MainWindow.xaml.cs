using System.Windows;
using System.Windows.Input;
using MicFX.ViewModels;

namespace MicFX;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    // Minimize to tray instead of taskbar
    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
            Hide();
    }

    // F1 = panic mute (toggle monitor mute)
    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.F1 && DataContext is MainViewModel vm)
        {
            vm.DeviceVM.MonitorMuted = !vm.DeviceVM.MonitorMuted;
            e.Handled = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.Dispose();
        base.OnClosed(e);
    }
}
