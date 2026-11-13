using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DonatScreen
{
    /// <summary>Shared look for app windows. Layout is written in 96-DPI units and scaled with <see cref="K"/>.</summary>
    abstract class ThemedForm : Form
    {
        protected readonly ToolTip Tips = new ToolTip();

        protected ThemedForm()
        {
            Text = "Donat Screen";
            Icon = Theme.AppIcon;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = MinimizeBox = false;
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Font = Theme.Normal;
            AutoScaleMode = AutoScaleMode.None; // we scale by hand so rebuilt controls get the same treatment
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int dark = Theme.Dark ? 1 : 0;
            Native.DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
        }

        // Fonts are sized at the system DPI (GDI hands that to per-monitor-aware apps), but on .NET Framework without
        // an app.config opt-in DeviceDpi stays 96: at 125 % text came out 25 % too big for its boxes and got clipped.
        // Scaling the layout by the system DPI keeps boxes and text in step.
        static readonly float Scale = SystemDpi() / 96f;

        static float SystemDpi()
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) return g.DpiY;
        }

        protected int K(int value) => (int)Math.Round(value * Scale);

        protected T Put<T>(Control parent, T control, int x, int y, int w, int h) where T : Control
        {
            control.SetBounds(K(x), K(y), K(w), K(h));
            parent.Controls.Add(control);
            return control;
        }

        protected T Put<T>(T control, int x, int y, int w, int h) where T : Control => Put(this, control, x, y, w, h);

        protected Label Caption(string text, int y) =>
            Put(new Label { Text = text, ForeColor = Theme.Text, Font = Theme.Caption }, 24, y, 392, 20);

        protected Label Note(string text, int x, int y, int w, int h, ContentAlignment align = ContentAlignment.MiddleLeft) =>
            Put(new Label { Text = text, ForeColor = Theme.Muted, Font = Theme.Small, TextAlign = align }, x, y, w, h);

        protected static Button IconButton(string glyph, Color color, Action click)
        {
            var b = FlatButton(glyph, click);
            b.Font = Theme.Symbols;
            b.ForeColor = color;
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        protected static Button FlatButton(string text, Action click, bool primary = false)
        {
            var b = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Theme.Accent : Theme.Surface,
                ForeColor = primary ? Color.White : Theme.Text,
                Cursor = Cursors.Hand,
            };
            b.FlatAppearance.BorderColor = primary ? Theme.Accent : Theme.Border;
            b.FlatAppearance.MouseOverBackColor = primary ? Theme.AccentHover : Theme.Hover;
            b.FlatAppearance.MouseDownBackColor = primary ? Theme.Accent : Theme.Border;
            b.Click += (o, e) => click();
            return b;
        }

        public const int VisibleRows = 5;

        /// <summary>List area that shows at most <see cref="VisibleRows"/> rows, then scrolls with a theme-matching scrollbar.</summary>
        protected Panel ScrollList(int y, int rows, int step, int extra, Color back)
        {
            var panel = Put(new Panel { BackColor = back, AutoScroll = true }, 24, y, 392, Math.Min(rows, VisibleRows) * step + extra);
            panel.HandleCreated += (o, e) => Native.SetWindowTheme(panel.Handle, Theme.Dark ? "DarkMode_Explorer" : "Explorer", null);
            return panel;
        }

        /// <summary>Row width inside a <see cref="ScrollList"/>, leaving room for the scrollbar when it shows.</summary>
        protected int RowWidth(int rows, int width) =>
            rows > VisibleRows ? width - (int)Math.Ceiling(SystemInformation.VerticalScrollBarWidth / Scale) : width;

        protected static void Outline(object sender, PaintEventArgs e) =>
            ControlPaint.DrawBorder(e.Graphics, ((Control)sender).ClientRectangle, Theme.Border, ButtonBorderStyle.Solid);

        protected override void Dispose(bool disposing)
        {
            if (disposing) Tips.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>Folder checkbox row: rounded check, the folder name over its path in muted text, ★ at the right for the default.</summary>
    sealed class FolderCheck : CheckBox
    {
        public string Detail = "";
        public bool Star;      // marks the default folder
        public bool Bordered;  // standalone row (save dialog) rather than inside a list

        int Pad => Height / 4;
        int Box => Height * 2 / 5;

        /// <summary>Where the text column starts, in pixels; lets a caller overlay an editor on the second line.</summary>
        public int TextLeft => Pad * 2 + Box;

        /// <summary>When set, a click on the path line raises this instead of toggling the check.</summary>
        public event Action PathClicked;
        bool pathHit;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            pathHit = PathClicked != null && e.X >= TextLeft && e.Y > Height / 2;
            base.OnMouseDown(e);
        }

        protected override void OnClick(EventArgs e)
        {
            if (pathHit) { pathHit = false; PathClicked(); return; }
            base.OnClick(e); // toggles
        }

        public FolderCheck()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            BackColor = Theme.Surface;
        }

        protected override void OnCheckedChanged(EventArgs e)
        {
            Invalidate();
            base.OnCheckedChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int pad = Pad, box = Box;
            var check = new Rectangle(pad, (Height - box) / 2, box, box);
            using (var path = Theme.Rounded(check, Math.Max(2, box / 5)))
            {
                if (Checked)
                {
                    using (var b = new SolidBrush(Theme.Accent)) g.FillPath(b, path);
                    using var tick = new Pen(Color.White, Math.Max(1.5f, box / 8f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                    g.DrawLines(tick, new[]
                    {
                        new PointF(check.X + box * 0.26f, check.Y + box * 0.53f),
                        new PointF(check.X + box * 0.43f, check.Y + box * 0.70f),
                        new PointF(check.X + box * 0.75f, check.Y + box * 0.33f),
                    });
                }
                else using (var p = new Pen(Theme.Muted, 1.5f)) g.DrawPath(p, path);
            }
            if (Bordered)
            {
                var r = new Rectangle(0, 0, Width - 1, Height - 1);
                using var p = new Pen(Checked ? Theme.Accent : Theme.Border, Checked ? 2f : 1f);
                g.DrawRectangle(p, Checked ? Rectangle.Inflate(r, -1, -1) : r);
            }
            g.SmoothingMode = SmoothingMode.None;

            // Two lines so long names get the whole width: name on top, path below; ★ in its own column at the right.
            int x = TextLeft, star = Star ? Height * 3 / 4 : 0, w = Width - x - pad / 2 - star, mid = Height / 2;
            var flags = TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, w, mid + 2), Theme.Text, flags | TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis);
            if (Star)
                TextRenderer.DrawText(g, Theme.StarFill, Theme.Symbols, new Rectangle(Width - star - pad / 2, 0, star, Height), Theme.Accent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            Theme.DrawPath(g, Detail, new Rectangle(x, mid + 2, w, Height - mid - 2), TextFormatFlags.Top);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -3, -3));
        }
    }

    /// <summary>Checkbox drawn as a switch; keeps CheckBox keyboard and accessibility behaviour.</summary>
    sealed class Toggle : CheckBox
    {
        public Toggle()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent.BackColor);
            int h = Height * 5 / 8, w = h * 2;
            var track = new Rectangle(Width - w - 2, (Height - h) / 2, w, h);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, track.X - 8, Height), ForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Rounded(track, h / 2))
            {
                if (Checked) using (var b = new SolidBrush(Theme.Accent)) g.FillPath(b, path);
                else using (var p = new Pen(Theme.Muted, 1.5f)) g.DrawPath(p, path);
            }
            int d = h - 8;
            using (var b = new SolidBrush(Checked ? Color.White : Theme.Muted))
                g.FillEllipse(b, Checked ? track.Right - d - 4 : track.X + 4, track.Y + 4, d, d);
            if (Focused && ShowFocusCues)
            {
                var ring = Rectangle.Inflate(track, 2, 2);
                using (var path = Theme.Rounded(ring, ring.Height / 2))
                using (var p = new Pen(Theme.Text, 1f))
                    g.DrawPath(p, path);
            }
        }
    }

    /// <summary>Modern Explorer-style folder picker (FolderBrowserDialog on .NET Framework is the old tree view).</summary>
    static class FolderPicker
    {
        public static string Show(IntPtr owner, string start)
        {
            var dialog = (IFileDialog)new FileOpenDialog();
            try
            {
                dialog.GetOptions(out uint options);
                dialog.SetOptions(options | 0x20 | 0x40); // FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM
                if (Directory.Exists(start) && SHCreateItemFromParsingName(start, IntPtr.Zero, typeof(IShellItem).GUID, out var item) == 0)
                    dialog.SetFolder(item);
                if (dialog.Show(owner) != 0) return null; // cancelled
                dialog.GetResult(out var result);
                result.GetDisplayName(0x80058000, out var path); // SIGDN_FILESYSPATH
                return path;
            }
            finally
            {
                Marshal.ReleaseComObject(dialog);
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHCreateItemFromParsingName(string path, IntPtr bindCtx, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItem item);

        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        class FileOpenDialog { }

        // Vtable order matters; only the prefix up to GetResult is declared.
        [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IFileDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint count, IntPtr filters);
            void SetFileTypeIndex(uint index);
            void GetFileTypeIndex(out uint index);
            void Advise(IntPtr events, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint options);
            void GetOptions(out uint options);
            void SetDefaultFolder(IShellItem item);
            void SetFolder(IShellItem item);
            void GetFolder(out IShellItem item);
            void GetCurrentSelection(out IShellItem item);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem item);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItem
        {
            void BindToHandler(IntPtr bindCtx, [MarshalAs(UnmanagedType.LPStruct)] Guid handler, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem parent);
            void GetDisplayName(uint sigdn, [MarshalAs(UnmanagedType.LPWStr)] out string name);
        }
    }
}
