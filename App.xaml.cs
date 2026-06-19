namespace ShakeToBigCursor;

public partial class App : System.Windows.Application
{
    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, _) => ShakeToBigCursor.MainWindow.RestoreSystemCursorsSafe();
        TaskScheduler.UnobservedTaskException += (_, _) => ShakeToBigCursor.MainWindow.RestoreSystemCursorsSafe();
        DispatcherUnhandledException += (_, _) => ShakeToBigCursor.MainWindow.RestoreSystemCursorsSafe();
        Exit += (_, _) => ShakeToBigCursor.MainWindow.RestoreSystemCursorsSafe();
    }

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        if (e.Args.Any(arg => string.Equals(arg, "--enable-startup", StringComparison.OrdinalIgnoreCase)))
        {
            Settings.StartupManager.Apply(enabled: true);
        }

        base.OnStartup(e);
    }
}
