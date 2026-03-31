using System.Windows;
using System.Windows.Forms; // for NotifyIcon
using System.Drawing;

namespace MicFX;

public partial class App : System.Windows.Application
{
    private NotifyIcon? _trayIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
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
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
