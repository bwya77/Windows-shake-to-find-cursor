using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
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
    private const int CursorFrameCount = 64;
    private const double WiggleGate = 0.55;
    private const double ReleaseTauMilliseconds = 140;
    private const double FollowTauMilliseconds = 55;
    private const double TriggerPath = 900;
    private const double PathForFullSize = TriggerPath * 2.2;
    private const double NormalCursorHeight = 32;
    private const double MaxCursorHeight = NormalCursorHeight * 9;

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
    private readonly Queue<MouseSample> history = new();
    private readonly IntPtr[] cursorFrames = new IntPtr[CursorFrameCount];

    private DateTime lastFrameTime = DateTime.UtcNow;
    private double energy;
    private double currentCursorHeight = NormalCursorHeight;
    private int lastAppliedFrameIndex;
    private bool enlargedCursorApplied;

    public MainWindow()
    {
        InitializeComponent();

        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;

        notifyIcon = CreateNotifyIcon();
        RestoreSystemCursors();
        BuildCursorFrames();

        timer.Tick += OnTick;
        timer.Start();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        timer.Stop();
        RestoreEnlargedCursor();
        DestroyCursorFrames();
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
        var exitItem = new Forms.ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => Close();

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("ShakeToBigCursor");
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        return new Forms.NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = Drawing.SystemIcons.Application,
            Text = "ShakeToBigCursor - native cursor",
            Visible = true
        };
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var elapsed = Math.Clamp((now - lastFrameTime).TotalSeconds, 0.001, 0.05);
        lastFrameTime = now;

        if (IsMouseButtonDown())
        {
            history.Clear();
            energy = 0;
        }
        else if (TryGetCursorPosition(out var position))
        {
            AddSample(position, now);
        }

        DecayEnergy(elapsed);
        UpdateCursor(elapsed);
    }

    private void AddSample(Drawing.Point point, DateTime now)
    {
        history.Enqueue(new MouseSample(point, now));

        while (history.Count > 0 && (now - history.Peek().Time).TotalMilliseconds > HistoryWindowMilliseconds)
        {
            history.Dequeue();
        }

        var instantEnergy = ComputeInstantEnergy();
        if (instantEnergy > energy)
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

    private void DecayEnergy(double elapsed)
    {
        energy *= Math.Exp(-(elapsed * 1000) / ReleaseTauMilliseconds);
        if (energy < 0.001)
        {
            energy = 0;
        }
    }

    private void UpdateCursor(double elapsed)
    {
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
        return (int)Math.Round(scale * (CursorFrameCount - 1));
    }

    private void ApplyFrame(int frameIndex)
    {
        frameIndex = Math.Clamp(frameIndex, 0, CursorFrameCount - 1);
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
        for (var i = 0; i < cursorFrames.Length; i++)
        {
            var t = i / (double)(cursorFrames.Length - 1);
            var biased = Math.Pow(t, 1.8);
            var height = (int)Math.Round(NormalCursorHeight + (biased * (MaxCursorHeight - NormalCursorHeight)));
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
