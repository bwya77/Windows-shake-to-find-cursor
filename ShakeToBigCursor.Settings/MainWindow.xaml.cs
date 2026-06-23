using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using ShakeToBigCursor.Settings;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;

namespace ShakeToBigCursor.SettingsApp;

public sealed partial class MainWindow : Window
{
    private readonly SettingsStore store = new();
    private bool loading;

    public MainWindow()
    {
        InitializeComponent();

        Title = "Windows Shake to Find Cursor Settings";
        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ResizeForDpi(860, 640);
        EnforceMinimumSize(680, 500);
        TrySetWindowIcon();

        loading = true;
        ConfigureSliders();
        LoadFrom(store.Current);
        store.Changed += OnStoreChanged;
        Closed += (_, _) =>
        {
            store.Changed -= OnStoreChanged;
            store.Dispose();
        };

        UpdateCaptionButtonColors();
        RootGrid.ActualThemeChanged += (_, _) => UpdateCaptionButtonColors();
    }

    private void ConfigureSliders()
    {
        MaxSizeSlider.Maximum = 512;
        MaxSizeSlider.Minimum = 64;
        MaxSizeSlider.StepFrequency = 4;
        MaxSizeSlider.TickFrequency = 4;

        ActivationSlider.Maximum = 2000;
        ActivationSlider.Minimum = 250;
        ActivationSlider.StepFrequency = 50;
        ActivationSlider.TickFrequency = 50;

        ReleaseHoldSlider.Maximum = 1500;
        ReleaseHoldSlider.Minimum = 0;
        ReleaseHoldSlider.StepFrequency = 50;
        ReleaseHoldSlider.TickFrequency = 50;
    }

    private void LoadFrom(AppSettings settings)
    {
        loading = true;
        settings.Normalize();
        MaxSizeSlider.Value = settings.MaxCursorHeight;
        ActivationSlider.Value = settings.ActivationDelayMilliseconds;
        ReleaseHoldSlider.Value = settings.ReleaseHoldMilliseconds;
        UpdateValueText(settings);
        loading = false;
    }

    private void UpdateValueText(AppSettings settings)
    {
        MaxSizeValueText.Text = $"{settings.MaxCursorHeight}px";
        ActivationValueText.Text = FormatMilliseconds(settings.ActivationDelayMilliseconds);
        ReleaseHoldValueText.Text = FormatMilliseconds(settings.ReleaseHoldMilliseconds);
    }

    private void SaveCurrent()
    {
        if (loading)
        {
            return;
        }

        var settings = new AppSettings
        {
            MaxCursorHeight = Snap(MaxSizeSlider.Value, 4),
            ActivationDelayMilliseconds = Snap(ActivationSlider.Value, 50),
            ReleaseHoldMilliseconds = Snap(ReleaseHoldSlider.Value, 50),
        };
        settings.Normalize();
        UpdateValueText(settings);
        store.Save(settings);
    }

    private void OnMaxSizeChanged(object sender, RangeBaseValueChangedEventArgs e) => SaveCurrent();

    private void OnActivationChanged(object sender, RangeBaseValueChangedEventArgs e) => SaveCurrent();

    private void OnReleaseHoldChanged(object sender, RangeBaseValueChangedEventArgs e) => SaveCurrent();

    private void OnRestoreDefaults(object sender, RoutedEventArgs e)
    {
        var defaults = new AppSettings();
        store.Save(defaults);
        LoadFrom(defaults);
    }

    private void OnStoreChanged(AppSettings settings)
    {
        DispatcherQueue.TryEnqueue(() => LoadFrom(settings));
    }

    private static int Snap(double value, int step)
    {
        return (int)Math.Round(value / step) * step;
    }

    private static string FormatMilliseconds(int milliseconds)
    {
        return milliseconds >= 1000
            ? $"{milliseconds / 1000.0:0.##}s"
            : $"{milliseconds} ms";
    }

    private void ResizeForDpi(int logicalWidth, int logicalHeight)
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var dpi = GetDpiForWindow(hwnd);
        var scale = dpi <= 0 ? 1.0 : dpi / 96.0;
        AppWindow.Resize(new SizeInt32(
            (int)Math.Round(logicalWidth * scale),
            (int)Math.Round(logicalHeight * scale)));
    }

    private void EnforceMinimumSize(int minWidth, int minHeight)
    {
        AppWindow.Changed += (s, e) =>
        {
            if (!e.DidSizeChange)
            {
                return;
            }

            var dpi = GetDpiForWindow(WindowNative.GetWindowHandle(this));
            var scale = dpi <= 0 ? 1.0 : dpi / 96.0;
            var minW = (int)Math.Round(minWidth * scale);
            var minH = (int)Math.Round(minHeight * scale);
            var size = s.Size;
            if (size.Width < minW || size.Height < minH)
            {
                s.Resize(new SizeInt32(Math.Max(size.Width, minW), Math.Max(size.Height, minH)));
            }
        };
    }

    private void TrySetWindowIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
        if (File.Exists(iconPath))
        {
            try
            {
                AppWindow.SetIcon(iconPath);
            }
            catch
            {
                // Non-fatal: the settings window can run without a custom icon.
            }
        }
    }

    private void UpdateCaptionButtonColors()
    {
        try
        {
            var titleBar = AppWindow.TitleBar;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

            var foreground = RootGrid.ActualTheme == ElementTheme.Dark ? Colors.White : Colors.Black;
            titleBar.ButtonForegroundColor = foreground;
            titleBar.ButtonHoverForegroundColor = foreground;
            titleBar.ButtonPressedForegroundColor = foreground;
            titleBar.ButtonInactiveForegroundColor = Color.FromArgb(255, 130, 130, 130);
        }
        catch
        {
            // Caption customization is best-effort across Windows builds.
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
