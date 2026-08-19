using System.IO;
using System.Windows;
using System.Windows.Threading;
using Lucent.Updates;

namespace Lucent;

public partial class App : Application
{
    public static string UserDataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Lucent", "WebView2");

    public static Browser Browser { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        Directory.CreateDirectory(UserDataFolder);

        Updater.RemoveRetired();

        DispatcherUnhandledException += OnUnhandledException;

        base.OnStartup(e);

        Browser = new Browser();
        new MainWindow(Browser).Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Browser?.History.Flush();
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"Lucent hit an unexpected error and needs to close.\n\n{e.Exception.Message}",
            "Lucent",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
        Shutdown();
    }
}
