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
}
