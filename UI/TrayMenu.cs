using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ShakeToBigCursor.UI;

internal static class TrayMenu
{
    private static Color Surface = Color.FromArgb(0x2B, 0x2B, 0x2B);
    private static Color Text = Color.FromArgb(0xE8, 0xE8, 0xE8);
    private static Color TextDim = Color.FromArgb(0x9A, 0x9A, 0x9A);
    private static Color Hover = Color.FromArgb(0x3A, 0x3A, 0x3A);
    private static Color Separator = Color.FromArgb(0x41, 0x41, 0x41);

    public static ContextMenuStrip Create(
        Func<bool> getLaunchAtLogin,
        Func<(bool available, string? latest)> getUpdate,
        Action onOpenSettings,
        Action onInstallUpdate,
        Action onCheckUpdates,
        Action<bool> onLaunchAtLoginChanged,
        Action onQuit)
    {
        ApplyPalette(SystemUsesLightTheme());

        var menu = new ContextMenuStrip
        {
            RenderMode = ToolStripRenderMode.Professional,
            ShowImageMargin = true,
            ShowCheckMargin = false,
            DropShadowEnabled = true,
            BackColor = Surface,
            ForeColor = Text,
            Font = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point),
            Padding = new Padding(0, 7, 0, 7),
            Renderer = new Win11Renderer(),
        };

        var update = NewItem("Update available", onInstallUpdate);
        update.Font = new Font(menu.Font, FontStyle.Bold);
        var updateSep = new ToolStripSeparator();
        var settings = NewItem("Settings", onOpenSettings);
        var check = NewItem("Check for updates", onCheckUpdates);
        var startup = NewItem("Start at login", () => onLaunchAtLoginChanged(!getLaunchAtLogin()));
        startup.CheckOnClick = false;
        var quit = NewItem("Quit", onQuit);

        menu.Items.Add(update);
        menu.Items.Add(updateSep);
        menu.Items.Add(settings);
        menu.Items.Add(check);
        menu.Items.Add(startup);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(quit);

        menu.Opening += (_, _) =>
        {
            ApplyPalette(SystemUsesLightTheme());
            menu.BackColor = Surface;
            menu.ForeColor = Text;

            var (available, latest) = getUpdate();
            update.Text = available && !string.IsNullOrWhiteSpace(latest) ? $"Install {latest}" : "Update available";
            update.Visible = available;
            updateSep.Visible = available;
            startup.Checked = getLaunchAtLogin();
        };

        menu.HandleCreated += (_, _) => ApplyWindowTheme(menu.Handle);
        menu.Opened += (_, _) => ApplyWindowTheme(menu.Handle);

        return menu;
    }

    private static ToolStripMenuItem NewItem(string text, Action onClick)
    {
        var item = new TallItem(text);
        item.Click += (_, _) => onClick();
        return item;
    }

    private static void ApplyPalette(bool light)
    {
        Surface = light ? Color.FromArgb(0xF9, 0xF9, 0xF9) : Color.FromArgb(0x2B, 0x2B, 0x2B);
        Text = light ? Color.FromArgb(0x1A, 0x1A, 0x1A) : Color.FromArgb(0xE8, 0xE8, 0xE8);
        TextDim = light ? Color.FromArgb(0x6A, 0x6A, 0x6A) : Color.FromArgb(0x9A, 0x9A, 0x9A);
        Hover = light ? Color.FromArgb(0xE6, 0xE6, 0xE6) : Color.FromArgb(0x3A, 0x3A, 0x3A);
        Separator = light ? Color.FromArgb(0xD9, 0xD9, 0xD9) : Color.FromArgb(0x41, 0x41, 0x41);
    }

    private static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
        }
        catch
        {
            return false;
        }
    }

    private static void ApplyWindowTheme(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var dark = SystemUsesLightTheme() ? 0 : 1;
        DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
        var round = 2;
        DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private sealed class TallItem : ToolStripMenuItem
    {
        public TallItem(string text) : base(text)
        {
        }

        public override Size GetPreferredSize(Size constrainingSize)
        {
            var size = base.GetPreferredSize(constrainingSize);
            size.Height += 14;
            return size;
        }
    }

    private sealed class Win11Colors : ProfessionalColorTable
    {
        public Win11Colors()
        {
            UseSystemColors = false;
        }

        public override Color ToolStripDropDownBackground => Surface;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color MenuBorder => Separator;
        public override Color MenuItemBorder => Hover;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color SeparatorDark => Separator;
        public override Color SeparatorLight => Separator;
        public override Color CheckBackground => Hover;
        public override Color CheckSelectedBackground => Hover;
        public override Color CheckPressedBackground => Hover;
    }

    private sealed class Win11Renderer : ToolStripProfessionalRenderer
    {
        public Win11Renderer() : base(new Win11Colors())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Text : TextDim;
            var rect = e.TextRectangle;
            e.TextRectangle = new Rectangle(rect.X, 0, rect.Width, e.Item.Height);
            e.TextFormat |= TextFormatFlags.VerticalCenter;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item.Selected && e.Item.Enabled)
            {
                var rect = new Rectangle(5, 3, e.Item.Width - 10, e.Item.Height - 6);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var path = Rounded(rect, 5);
                using var brush = new SolidBrush(Hover);
                e.Graphics.FillPath(brush, path);
                return;
            }

            using var background = new SolidBrush(Surface);
            e.Graphics.FillRectangle(background, new Rectangle(Point.Empty, e.Item.Size));
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(Surface);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(Surface);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var rect = e.Item.ContentRectangle;
            var y = rect.Top + rect.Height / 2;
            using var pen = new Pen(Separator);
            e.Graphics.DrawLine(pen, rect.Left + 8, y, e.Item.Width - 8, y);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            const int box = 16;
            var centerX = e.ImageRectangle.Left + e.ImageRectangle.Width / 2;
            var centerY = e.Item.Height / 2;
            var rect = new Rectangle(centerX - box / 2, centerY - box / 2, box, box);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Text, 1.7f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round,
            };
            var x = rect.Left + rect.Width * 0.20f;
            var y = rect.Top + rect.Height * 0.54f;
            e.Graphics.DrawLines(pen,
            [
                new PointF(x, y),
                new PointF(x + rect.Width * 0.18f, y + rect.Height * 0.20f),
                new PointF(x + rect.Width * 0.54f, y - rect.Height * 0.30f),
            ]);
        }

        private static GraphicsPath Rounded(Rectangle rect, int radius)
        {
            var diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
