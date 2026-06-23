using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using ShakeToBigCursor.Settings;
using ShakeToBigCursor.UI;
using ShakeToBigCursor.Updates;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace ShakeToBigCursor;

public partial class MainWindow : Window
{
    private const int SpiSetCursors = 0x0057;
    private const int VkLeftButton = 0x01;
    private const int VkRightButton = 0x02;
    private const int VkMiddleButton = 0x04;

    private const int HistoryWindowMilliseconds = 350;
    private const double WiggleGate = 0.55;
    private const double ReleaseShrinkMilliseconds = 550;
    private const double ReleaseTauMilliseconds = 140;
    private const double FollowTauMilliseconds = 140;
    private const double TriggerPath = 900;
    private const double PathForFullSize = TriggerPath * 2.2;
    private const double NormalCursorHeight = 32;

    private static readonly uint[] SystemCursorIds =
    [
        32512, // Normal
        32513, // I-beam
        32514, // Wait
        32515, // Cross
        32516, // Up
        32642, // Size northwest/southeast
        32643, // Size northeast/southwest
        32644, // Size west/east
        32645, // Size north/south
        32646, // Size all
        32648, // No
        32649, // Hand
        32650, // App starting
        32651  // Help
    ];

    private readonly DispatcherTimer timer = new()
    {
        Interval = TimeSpan.FromMilliseconds(16)
    };

    private readonly Forms.NotifyIcon notifyIcon;
    private readonly UpdateChecker updateChecker = new();
    private readonly SettingsStore settingsStore = new();
    private readonly Queue<MouseSample> history = new();
    private IntPtr[] cursorFrames = [];

    private UpdateChecker.UpdateInfo? pendingUpdate;
    private AppSettings settings = new();
    private DateTime lastFrameTime = DateTime.UtcNow;
    private double energy;
    private double pendingEnergy;
    private double currentCursorHeight = NormalCursorHeight;
    private int lastAppliedFrameIndex;
    private bool enlargedCursorApplied;
    private bool shakeActivated;
    private bool activeShakeThisTick;
    private bool releaseHoldCompleted;
    private bool releaseShrinking;
    private DateTime? shakeStartedAt;
    private DateTime? releaseHoldUntil;
    private DateTime? releaseShrinkStartedAt;
    private double releaseShrinkStartHeight = NormalCursorHeight;

    public MainWindow()
    {
        InitializeComponent();

        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;

        StartupManager.Apply(StartupManager.IsEnabled());
        settings = settingsStore.Current.Clone();
        settings.Normalize();
        settingsStore.Changed += OnSettingsChanged;
        notifyIcon = CreateNotifyIcon();
        RestoreSystemCursors();
        BuildCursorFrames();

        timer.Tick += OnTick;
        timer.Start();
        _ = CheckForUpdatesAsync(manual: false);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        timer.Stop();
        RestoreEnlargedCursor();
        DestroyCursorFrames();
        settingsStore.Changed -= OnSettingsChanged;
        settingsStore.Dispose();
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        base.OnClosing(e);
    }

    public static void RestoreSystemCursorsSafe()
    {
        SystemParametersInfo(SpiSetCursors, 0, IntPtr.Zero, 0);
    }

    private Forms.NotifyIcon CreateNotifyIcon()
    {
        return new Forms.NotifyIcon
        {
            ContextMenuStrip = TrayMenu.Create(
                getLaunchAtLogin: StartupManager.IsEnabled,
                getUpdate: () => (pendingUpdate != null, pendingUpdate?.Tag),
                onOpenSettings: OpenSettings,
                onInstallUpdate: () => _ = InstallUpdateAsync(),
                onCheckUpdates: () => _ = CheckForUpdatesAsync(manual: true),
                onLaunchAtLoginChanged: ToggleLaunchAtLogin,
                onQuit: Close),
            Icon = LoadTrayIcon(),
            Text = "ShakeToBigCursor - native cursor",
            Visible = true
        };
    }

    private double MaxCursorHeight => settings.MaxCursorHeight;

    private double ActivationDelayMilliseconds => settings.ActivationDelayMilliseconds;

    private double ReleaseHoldMilliseconds => settings.ReleaseHoldMilliseconds;

    private void OnSettingsChanged(AppSettings updated)
    {
        Dispatcher.BeginInvoke(() => ApplySettings(updated));
    }

    private void ApplySettings(AppSettings updated)
    {
        updated.Normalize();
        var previousMaxCursorHeight = settings.MaxCursorHeight;
        settings = updated.Clone();

        if (settings.MaxCursorHeight != previousMaxCursorHeight)
        {
            RestoreEnlargedCursor();
            DestroyCursorFrames();
            currentCursorHeight = NormalCursorHeight;
            lastAppliedFrameIndex = 0;
            BuildCursorFrames();
        }
    }

    private void OpenSettings()
    {
        var settingsExe = Path.Combine(AppContext.BaseDirectory, "Settings", "ShakeToBigCursor.Settings.exe");
        if (!File.Exists(settingsExe))
        {
            settingsExe = Path.Combine(AppContext.BaseDirectory, "ShakeToBigCursor.Settings.exe");
        }

        if (!File.Exists(settingsExe))
        {
            settingsExe = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                @"..\..\..\..\ShakeToBigCursor.Settings\bin\Debug\net8.0-windows10.0.19041.0\ShakeToBigCursor.Settings.exe"));
        }

        if (!File.Exists(settingsExe))
        {
            notifyIcon.ShowBalloonTip(
                3500,
                "Windows Shake to Find Cursor",
                "Settings are not available in this build.",
                Forms.ToolTipIcon.Warning);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = settingsExe,
            UseShellExecute = true,
        });
    }

    private static Drawing.Icon LoadTrayIcon()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("icon.ico");
        if (stream == null)
        {
            return Drawing.SystemIcons.Application;
        }

        return new Drawing.Icon(stream);
    }

    private void ToggleLaunchAtLogin(bool enabled)
    {
        StartupManager.Apply(enabled);
        notifyIcon.ShowBalloonTip(
            2500,
            "Windows Shake to Find Cursor",
            enabled ? "Start at login is enabled." : "Start at login is disabled.",
            Forms.ToolTipIcon.Info);
    }

    private async Task CheckForUpdatesAsync(bool manual)
    {
        var info = await updateChecker.CheckAsync().ConfigureAwait(true);
        if (info == null)
        {
            pendingUpdate = null;

            if (manual)
            {
                notifyIcon.ShowBalloonTip(
                    3500,
                    "Windows Shake to Find Cursor",
                    $"You're on the latest version (v{UpdateChecker.CurrentVersion.ToString(3)}).",
                    Forms.ToolTipIcon.Info);
            }

            return;
        }

        pendingUpdate = info;
        notifyIcon.ShowBalloonTip(
            10000,
            "Update available",
            $"Version {info.Latest.ToString(3)} is ready. Click here to install.",
            Forms.ToolTipIcon.Info);
    }

    private async Task InstallUpdateAsync()
    {
        var info = pendingUpdate ?? await updateChecker.CheckAsync().ConfigureAwait(true);
        if (info == null)
        {
            notifyIcon.ShowBalloonTip(
                3500,
                "Windows Shake to Find Cursor",
                "No update is available.",
                Forms.ToolTipIcon.Info);
            return;
        }

        if (string.IsNullOrWhiteSpace(info.InstallerUrl))
        {
            Process.Start(new ProcessStartInfo(info.HtmlUrl) { UseShellExecute = true });
            return;
        }

        try
        {
            var installerPath = Path.Combine(Path.GetTempPath(), $"WindowsShakeToFindCursor-{info.Tag}.exe");
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            await using (var source = await http.GetStreamAsync(info.InstallerUrl).ConfigureAwait(true))
            await using (var destination = File.Create(installerPath))
            {
                await source.CopyToAsync(destination).ConfigureAwait(true);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/SILENT /NORESTART",
                UseShellExecute = true,
                Verb = "runas",
            });
            Close();
        }
        catch
        {
            notifyIcon.ShowBalloonTip(
                5000,
                "Update failed",
                "Could not download or start the installer. Opening the release page instead.",
                Forms.ToolTipIcon.Warning);
            Process.Start(new ProcessStartInfo(info.HtmlUrl) { UseShellExecute = true });
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var elapsed = Math.Clamp((now - lastFrameTime).TotalSeconds, 0.001, 0.05);
        lastFrameTime = now;
        activeShakeThisTick = false;

        if (IsMouseButtonDown())
        {
            history.Clear();
            ResetShake();
        }
        else if (TryGetCursorPosition(out var position))
        {
            AddSample(position, now);
        }

        DecayEnergy(elapsed, now);
        UpdateCursor(elapsed, now);
    }

    private void AddSample(Drawing.Point point, DateTime now)
    {
        history.Enqueue(new MouseSample(point, now));

        while (history.Count > 0 && (now - history.Peek().Time).TotalMilliseconds > HistoryWindowMilliseconds)
        {
            history.Dequeue();
        }

        var instantEnergy = ComputeInstantEnergy();
        if (instantEnergy <= 0)
        {
            if (shakeActivated || releaseHoldUntil.HasValue || (!releaseHoldCompleted && energy > 0))
            {
                BeginReleaseHold(now);
            }
            else if (energy == 0 && !releaseShrinking)
            {
                ResetShake();
            }

            return;
        }

        releaseHoldUntil = null;
        releaseHoldCompleted = false;
        releaseShrinking = false;
        releaseShrinkStartedAt = null;
        shakeStartedAt ??= history.Peek().Time;
        pendingEnergy = Math.Max(pendingEnergy, instantEnergy);

        if (!shakeActivated && (now - shakeStartedAt.Value).TotalMilliseconds >= ActivationDelayMilliseconds)
        {
            shakeActivated = true;
            energy = Math.Max(energy, pendingEnergy);
        }

        activeShakeThisTick = shakeActivated;

        if (shakeActivated && instantEnergy > energy)
        {
            energy = instantEnergy;
        }
    }

    private double ComputeInstantEnergy()
    {
        if (history.Count < 5)
        {
            return 0;
        }

        var samples = history.ToArray();
        var first = samples[0].Point;
        var last = samples[^1].Point;
        var previous = first;
        var totalPath = 0.0;
        var reversals = 0;
        var previousDx = 0;
        var previousDy = 0;
        var hasPreviousSegment = false;

        for (var i = 1; i < samples.Length; i++)
        {
            var point = samples[i].Point;
            var dx = point.X - previous.X;
            var dy = point.Y - previous.Y;

            totalPath += Math.Sqrt((dx * dx) + (dy * dy));

            if (hasPreviousSegment && ((dx * previousDx) + (dy * previousDy)) < 0)
            {
                reversals++;
            }

            if (dx != 0 || dy != 0)
            {
                previousDx = dx;
                previousDy = dy;
                hasPreviousSegment = true;
            }

            previous = point;
        }

        if (totalPath < TriggerPath || reversals < 2)
        {
            return 0;
        }

        var netDistance = Distance(first, last);
        var wiggle = totalPath > 0 ? 1 - (netDistance / totalPath) : 0;
        if (wiggle < WiggleGate)
        {
            return 0;
        }

        return Math.Clamp((totalPath - TriggerPath) / (PathForFullSize - TriggerPath), 0, 1);
    }

    private void DecayEnergy(double elapsed, DateTime now)
    {
        if (releaseHoldUntil.HasValue)
        {
            if (now < releaseHoldUntil.Value)
            {
                return;
            }

            releaseHoldUntil = null;
            releaseHoldCompleted = true;
            BeginReleaseShrink(now);
            return;
        }

        if (releaseShrinking)
        {
            return;
        }

        if (activeShakeThisTick)
        {
            return;
        }

        energy *= Math.Exp(-(elapsed * 1000) / ReleaseTauMilliseconds);
        if (energy < 0.001)
        {
            energy = 0;
        }
    }

    private void BeginReleaseHold(DateTime now)
    {
        pendingEnergy = 0;
        shakeActivated = false;
        activeShakeThisTick = false;
        shakeStartedAt = null;
        releaseHoldUntil ??= now.AddMilliseconds(ReleaseHoldMilliseconds);
    }

    private void BeginReleaseShrink(DateTime now)
    {
        energy = 0;
        releaseShrinking = true;
        releaseShrinkStartedAt = now;
        releaseShrinkStartHeight = currentCursorHeight;
    }

    private void ResetShake()
    {
        energy = 0;
        pendingEnergy = 0;
        shakeActivated = false;
        activeShakeThisTick = false;
        releaseHoldCompleted = false;
        releaseShrinking = false;
        shakeStartedAt = null;
        releaseHoldUntil = null;
        releaseShrinkStartedAt = null;
        releaseShrinkStartHeight = NormalCursorHeight;
    }

    private void UpdateCursor(double elapsed, DateTime now)
    {
        if (releaseHoldUntil.HasValue)
        {
            if (currentCursorHeight > NormalCursorHeight + 0.5)
            {
                ApplyFrame(GetFrameIndexForHeight(currentCursorHeight));
            }

            return;
        }

        if (releaseShrinking && releaseShrinkStartedAt.HasValue)
        {
            var progress = Math.Clamp((now - releaseShrinkStartedAt.Value).TotalMilliseconds / ReleaseShrinkMilliseconds, 0, 1);
            currentCursorHeight = releaseShrinkStartHeight + ((NormalCursorHeight - releaseShrinkStartHeight) * progress);

            if (progress >= 1 || currentCursorHeight <= NormalCursorHeight + 0.5)
            {
                currentCursorHeight = NormalCursorHeight;
                RestoreEnlargedCursor();
                releaseShrinking = false;
                releaseShrinkStartedAt = null;
                return;
            }

            ApplyFrame(GetFrameIndexForHeight(currentCursorHeight));
            return;
        }

        var targetHeight = NormalCursorHeight + (energy * (MaxCursorHeight - NormalCursorHeight));
        var alpha = 1 - Math.Exp(-(elapsed * 1000) / FollowTauMilliseconds);
        currentCursorHeight += (targetHeight - currentCursorHeight) * alpha;
        currentCursorHeight = Math.Max(NormalCursorHeight, currentCursorHeight);

        if (energy == 0 && currentCursorHeight <= NormalCursorHeight + 0.5)
        {
            currentCursorHeight = NormalCursorHeight;
            RestoreEnlargedCursor();
            return;
        }

        if (currentCursorHeight > NormalCursorHeight + 0.5)
        {
            ApplyFrame(GetFrameIndexForHeight(currentCursorHeight));
        }
    }

    private int GetFrameIndexForHeight(double cursorHeight)
    {
        var scale = Math.Clamp((cursorHeight - NormalCursorHeight) / (MaxCursorHeight - NormalCursorHeight), 0, 1);
        return (int)Math.Round(scale * (cursorFrames.Length - 1));
    }

    private void ApplyFrame(int frameIndex)
    {
        frameIndex = Math.Clamp(frameIndex, 0, cursorFrames.Length - 1);
        if (frameIndex == lastAppliedFrameIndex && enlargedCursorApplied)
        {
            return;
        }

        var frame = cursorFrames[frameIndex];
        if (frame == IntPtr.Zero)
        {
            RestoreSystemCursors();
            throw new InvalidOperationException($"Cursor frame {frameIndex} was not generated.");
        }

        foreach (var cursorId in SystemCursorIds)
        {
            var copy = CopyIcon(frame);
            if (copy == IntPtr.Zero)
            {
                RestoreSystemCursors();
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not copy the cached cursor frame.");
            }

            if (!SetSystemCursor(copy, cursorId))
            {
                DestroyIcon(copy);
                RestoreSystemCursors();
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not apply the cached cursor frame.");
            }
        }

        enlargedCursorApplied = true;
        lastAppliedFrameIndex = frameIndex;
    }

    private void RestoreEnlargedCursor()
    {
        if (!enlargedCursorApplied)
        {
            return;
        }

        RestoreSystemCursors();
        enlargedCursorApplied = false;
        lastAppliedFrameIndex = 0;
    }

    private void BuildCursorFrames()
    {
        var frameCount = Math.Max(2, (int)Math.Round(MaxCursorHeight - NormalCursorHeight) + 1);
        cursorFrames = new IntPtr[frameCount];
        for (var i = 0; i < cursorFrames.Length; i++)
        {
            var t = i / (double)(cursorFrames.Length - 1);
            var height = (int)Math.Round(NormalCursorHeight + (t * (MaxCursorHeight - NormalCursorHeight)));
            cursorFrames[i] = CreateArrowCursor(height);

            if (cursorFrames[i] == IntPtr.Zero)
            {
                DestroyCursorFrames();
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not generate cursor frame {i}.");
            }
        }
    }

    private void DestroyCursorFrames()
    {
        for (var i = 0; i < cursorFrames.Length; i++)
        {
            if (cursorFrames[i] == IntPtr.Zero)
            {
                continue;
            }

            DestroyIcon(cursorFrames[i]);
            cursorFrames[i] = IntPtr.Zero;
        }
    }

    private static IntPtr CreateArrowCursor(int height)
    {
        var width = Math.Max(24, (int)Math.Round(height * 0.73));
        using var bitmap = new Drawing.Bitmap(width, height, Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Drawing.Graphics.FromImage(bitmap);

        graphics.Clear(Drawing.Color.Transparent);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var scaleX = width / 66f;
        var scaleY = height / 90f;
        Drawing.PointF[] points =
        [
            new(0 * scaleX, 0 * scaleY),
            new(0 * scaleX, 76 * scaleY),
            new(21 * scaleX, 56 * scaleY),
            new(35 * scaleX, 90 * scaleY),
            new(51 * scaleX, 84 * scaleY),
            new(37 * scaleX, 52 * scaleY),
            new(66 * scaleX, 52 * scaleY)
        ];

        using var fill = new Drawing.SolidBrush(Drawing.Color.White);
        using var stroke = new Drawing.Pen(Drawing.Color.Black, Math.Max(1.5f, height / 36f))
        {
            LineJoin = LineJoin.Round
        };

        graphics.FillPolygon(fill, points);
        graphics.DrawPolygon(stroke, points);

        var colorBitmap = bitmap.GetHbitmap(Drawing.Color.FromArgb(0));
        var maskBitmap = CreateBitmap(width, height, 1, 1, IntPtr.Zero);

        try
        {
            var iconInfo = new IconInfo
            {
                IsIcon = false,
                XHotspot = 0,
                YHotspot = 0,
                ColorBitmap = colorBitmap,
                MaskBitmap = maskBitmap
            };

            return CreateIconIndirect(ref iconInfo);
        }
        finally
        {
            DeleteObject(colorBitmap);
            DeleteObject(maskBitmap);
        }
    }

    private static bool IsMouseButtonDown()
    {
        return IsKeyDown(VkLeftButton) || IsKeyDown(VkRightButton) || IsKeyDown(VkMiddleButton);
    }

    private static bool IsKeyDown(int virtualKey)
    {
        return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    private static bool TryGetCursorPosition(out Drawing.Point point)
    {
        if (!GetCursorPos(out var nativePoint))
        {
            point = default;
            return false;
        }

        point = new Drawing.Point(nativePoint.X, nativePoint.Y);
        return true;
    }

    private static double Distance(Drawing.Point a, Drawing.Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static void RestoreSystemCursors()
    {
        if (!SystemParametersInfo(SpiSetCursors, 0, IntPtr.Zero, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not restore system cursors.");
        }
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetSystemCursor(IntPtr hcur, uint id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(int uiAction, int uiParam, IntPtr pvParam, int fWinIni);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateIconIndirect(ref IconInfo iconInfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CopyIcon(IntPtr hIcon);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitsPerPixel, IntPtr bits);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);

    private readonly record struct MouseSample(Drawing.Point Point, DateTime Time);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint
    {
        public readonly int X;
        public readonly int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool IsIcon;

        public int XHotspot;
        public int YHotspot;
        public IntPtr MaskBitmap;
        public IntPtr ColorBitmap;
    }
}
