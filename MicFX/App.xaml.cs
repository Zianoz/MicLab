using System.Windows;
using System.Windows.Forms; // for NotifyIcon
using System.Drawing;
using System.Windows.Threading;

namespace MicFX;

public partial class App : System.Windows.Application
{
    private NotifyIcon? _trayIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        base.OnStartup(e);
        SetupTrayIcon();
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new NotifyIcon
        {
            Text = "MicFX",
            Icon = SystemIcons.Application, // replaced by real icon in publish
            Visible = true
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Show", null, (_, _) => ShowMainWindow());
        menu.Items.Add("Exit", null, (_, _) => Shutdown());
        _trayIcon.ContextMenuStrip = menu;

        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        MainWindow?.Show();
        MainWindow?.Activate();
        MainWindow!.WindowState = WindowState.Normal;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        _trayIcon?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        ShowFatalError(e.Exception);
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            Current?.Dispatcher.Invoke(() => ShowFatalError(ex));
    }

    private void ShowFatalError(Exception ex)
    {
        if (MainWindow?.DataContext is ViewModels.MainViewModel vm)
            vm.StatusMessage = $"Unexpected error: {ex.Message}";

        System.Windows.MessageBox.Show(
            ex.Message,
            "MicFX Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
