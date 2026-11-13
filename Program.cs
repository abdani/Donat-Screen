using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DonatScreen
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            using var instance = new Mutex(true, "DonatScreen.Instance", out bool first);
            using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "DonatScreen.Show");
            if (!first)
            {
                // Already running: ask that instance to open its settings window.
                Native.AllowSetForegroundWindow(-1);
                showSignal.Set();
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new App(showSignal, args.Contains(Autostart.Flag)));
        }
    }

    sealed class App : ApplicationContext
    {
        const int RegionId = 1, FullId = 2, ProbeId = 99;

        public readonly Settings Settings = Settings.Load();
        readonly Hotkeys hotkeys = new Hotkeys();
        readonly NotifyIcon tray;
        readonly ToolStripMenuItem regionItem, fullItem;
        readonly List<(ToolStripMenuItem item, string text)> menuTexts = new List<(ToolStripMenuItem, string)>();
        readonly SynchronizationContext ui = new WindowsFormsSynchronizationContext();
        SettingsForm settingsForm;
        string lastShot;
        bool busy;

        public App(WaitHandle showSignal, bool autostarted)
        {
            bool firstRun = !File.Exists(Settings.FilePath);
            L.Indonesian = Settings.Lang == "id";
            Autostart.Repair();

            var menu = new ContextMenuStrip { Renderer = new FlatRenderer(), ShowImageMargin = false, Font = new Font("Segoe UI", 9.5f) };
            regionItem = Item(menu, "Ambil area", () => Capture(true, true));
            fullItem = Item(menu, "Ambil layar penuh", () => Capture(false, true));
            menu.Items.Add(new ToolStripSeparator());
            Item(menu, "Buka folder default", () => OpenFolder(Settings.Default.FullPath));
            Item(menu, "Pengaturan", ShowSettings);
            menu.Items.Add(new ToolStripSeparator());
            Item(menu, "Keluar", ExitThread);

            var small = new IntPtr[1];
            Native.ExtractIconEx(Application.ExecutablePath, 0, null, small, 1);
            tray = new NotifyIcon
            {
                Icon = small[0] != IntPtr.Zero ? Icon.FromHandle(small[0]) : Theme.AppIcon,
                Text = "Donat Screen",
                ContextMenuStrip = menu,
                Visible = true,
            };
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowSettings(); };
            tray.BalloonTipClicked += (s, e) =>
            {
                if (File.Exists(lastShot)) Process.Start("explorer.exe", $"/select,\"{lastShot}\"");
            };

            hotkeys.Pressed += id => Capture(id == RegionId, false);
            ThreadPool.RegisterWaitForSingleObject(showSignal, (o, t) => ui.Post(x => ShowSettings(), null), null, -1, false);

            var error = ApplyHotkeys();
            if (firstRun)
            {
                Settings.Save();
                ShowSettings();
            }
            else if (error != null || !autostarted) // stay quiet at Windows sign-in unless something is wrong
            {
                Notify(L.T("Donat Screen aktif"),
                    error ?? L.T("Area: {0}   ·   Layar penuh: {1}", Hotkeys.Format(Settings.RegionKey), Hotkeys.Format(Settings.FullKey)),
                    error != null);
            }
        }

        ToolStripMenuItem Item(ContextMenuStrip menu, string text, Action click)
        {
            var item = new ToolStripMenuItem(L.T(text), null, (s, e) => click()) { Padding = new Padding(4, 3, 4, 3) };
            menu.Items.Add(item);
            menuTexts.Add((item, text));
            return item;
        }

        /// <summary>Called after the language changes; open windows rebuild themselves.</summary>
        public void ApplyLanguage()
        {
            L.Indonesian = Settings.Lang == "id";
            foreach (var (item, text) in menuTexts) item.Text = L.T(text);
        }

        /// <summary>Re-registers both hotkeys; returns an error message, or null when all succeeded.</summary>
        public string ApplyHotkeys()
        {
            SuspendHotkeys();
            var failed = new List<string>();
            if (!hotkeys.Register(RegionId, Settings.RegionKey)) failed.Add(Hotkeys.Format(Settings.RegionKey));
            if (!hotkeys.Register(FullId, Settings.FullKey)) failed.Add(Hotkeys.Format(Settings.FullKey));
            regionItem.ShortcutKeyDisplayString = Hotkeys.Format(Settings.RegionKey);
            fullItem.ShortcutKeyDisplayString = Hotkeys.Format(Settings.FullKey);
            return failed.Count == 0 ? null : L.T("Shortcut {0} bentrok dengan aplikasi lain. Ganti di Pengaturan.", string.Join(", ", failed));
        }

        public void SuspendHotkeys()
        {
            hotkeys.Unregister(RegionId);
            hotkeys.Unregister(FullId);
        }

        /// <summary>True when Windows would let us own this combination right now.</summary>
        public bool CanRegister(Keys keys)
        {
            if (!hotkeys.Register(ProbeId, keys)) return false;
            hotkeys.Unregister(ProbeId);
            return true;
        }

        async void Capture(bool region, bool fromMenu)
        {
            if (busy) return;
            busy = true;
            try
            {
                if (fromMenu) await Task.Delay(300); // let the tray menu fade out first
                using var shot = Grab(SystemInformation.VirtualScreen);
                if (!region)
                {
                    Deliver(shot);
                    return;
                }
                using var overlay = new RegionOverlay(shot);
                if (overlay.ShowDialog() == DialogResult.OK)
                    using (overlay.Result) Deliver(overlay.Result);
            }
            catch (Exception e)
            {
                Notify(L.T("Screenshot gagal"), e.Message, true);
            }
            finally
            {
                busy = false;
            }
        }

        // ponytail: GDI capture of the composed desktop; exclusive-fullscreen DirectX games may come out black,
        // switch to Windows.Graphics.Capture if that matters.
        static Bitmap Grab(Rectangle area)
        {
            var bmp = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppRgb);
            int error = 0;
            using (var g = Graphics.FromImage(bmp))
            {
                // Raw BitBlt: Graphics.CopyFromScreen rejects SRCCOPY | CAPTUREBLT, and CAPTUREBLT is what
                // brings in layered windows (tooltips, menus, overlays).
                IntPtr dst = g.GetHdc(), src = Native.GetDC(IntPtr.Zero);
                if (!Native.BitBlt(dst, 0, 0, area.Width, area.Height, src, area.X, area.Y, 0x00CC0020 | 0x40000000))
                    error = Marshal.GetLastWin32Error();
                Native.ReleaseDC(IntPtr.Zero, src);
                g.ReleaseHdc(dst);
            }
            if (error == 0) return bmp;
            bmp.Dispose();
            throw new Win32Exception(error);
        }

        /// <summary>Clipboard first (every shot lands there, whatever happens next), then ask where to save if enabled.</summary>
        void Deliver(Bitmap bmp)
        {
            using var png = new MemoryStream();
            bmp.Save(png, ImageFormat.Png);
            bool copied = CopyToClipboard(bmp, png);

            var folders = new List<string> { Settings.Default.FullPath };
            string name = $"DonatScreen_{DateTime.Now:yyyyMMdd_HHmmss_fff}";
            if (Settings.AskEachTime)
            {
                using var dialog = new SaveDialog(bmp, Settings, name);
                dialog.ShowDialog(); // Esc / close keeps the default folder and automatic name
                folders = dialog.Folders;
                name = dialog.FileName;
            }

            var bytes = png.ToArray();
            var saved = new List<string>();
            var failed = new List<string>();
            foreach (var folder in folders)
            {
                try
                {
                    Directory.CreateDirectory(folder);
                    var path = UniquePath(folder, name);
                    File.WriteAllBytes(path, bytes);
                    saved.Add(path);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
                {
                    failed.Add($"{Destination.NameOf(folder)}: {e.Message}");
                }
            }

            var clip = copied ? L.T("Disalin ke clipboard") : L.T("Clipboard sedang dipakai aplikasi lain, tidak disalin");
            if (saved.Count > 0) lastShot = saved[0];
            if (failed.Count == 0)
            {
                var where = saved.Count == 1 ? Destination.NameOf(folders[0]) : string.Join(", ", folders.Select(Destination.NameOf));
                Notify(L.T("Tersimpan di {0}", where), $"{Path.GetFileName(saved[0])}\n{clip}", !copied);
            }
            else
            {
                Notify(saved.Count > 0 ? L.T("Sebagian gagal disimpan") : L.T("Gagal menyimpan file"),
                    string.Join("\n", failed) + (copied ? "\n" + L.T("Gambar tetap ada di clipboard.") : ""), true);
            }
        }

        /// <summary>Never overwrite: "laporan.png" taken → "laporan (2).png".</summary>
        static string UniquePath(string folder, string name)
        {
            var path = Path.Combine(folder, name + ".png");
            for (int i = 2; File.Exists(path); i++) path = Path.Combine(folder, $"{name} ({i}).png");
            return path;
        }

        static bool CopyToClipboard(Bitmap bmp, MemoryStream png)
        {
            // 24 bpp: a 32 bpp DIB has an undefined alpha byte, which some apps paste as a transparent image.
            using var rgb = bmp.Clone(new Rectangle(Point.Empty, bmp.Size), PixelFormat.Format24bppRgb);
            png.Position = 0;
            var data = new DataObject();
            data.SetData(DataFormats.Bitmap, true, rgb); // CF_BITMAP / CF_DIB: Word, Excel, PowerPoint, Paint, chat apps
            data.SetData("PNG", false, png);             // lossless PNG: browsers, Office, Google Docs, GIMP
            try
            {
                Clipboard.SetDataObject(data, true, 10, 50); // copy: survives our exit; retry while another app holds it
                return true;
            }
            catch (ExternalException)
            {
                return false;
            }
        }

        public void OpenFolder(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                Process.Start("explorer.exe", $"\"{path}\"");
            }
            catch (Exception e)
            {
                Notify(L.T("Folder tidak bisa dibuka"), e.Message, true);
            }
        }

        public void ShowSettings()
        {
            if (settingsForm == null)
            {
                settingsForm = new SettingsForm(this);
                settingsForm.FormClosed += (s, e) =>
                {
                    settingsForm = null;
                    var error = ApplyHotkeys();
                    if (error != null) Notify(L.T("Shortcut tidak aktif"), error, true);
                };
                settingsForm.Show();
            }
            settingsForm.Activate();
        }

        void Notify(string title, string text, bool warning) =>
            tray.ShowBalloonTip(3000, title, text, warning ? ToolTipIcon.Warning : ToolTipIcon.None);

        protected override void ExitThreadCore()
        {
            settingsForm?.Close();
            tray.Visible = false;
            hotkeys.DestroyHandle();
            base.ExitThreadCore();
        }
    }

    /// <summary>A save folder from the settings list.</summary>
    sealed class Destination
    {
        public string Path;          // as stored: relative to the exe folder when inside it, so a USB stick works under any drive letter
        public bool Visible = true;  // offered as a choice in the save dialog

        public string FullPath
        {
            get
            {
                try { return System.IO.Path.GetFullPath(System.IO.Path.Combine(Settings.BaseDir, Path)); }
                catch (Exception e) when (e is ArgumentException || e is NotSupportedException) { return Path; }
            }
        }

        public string Name => NameOf(FullPath);

        public static string NameOf(string path)
        {
            var name = System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
            return string.IsNullOrEmpty(name) ? path : name;
        }

        public static Destination FromFullPath(string full)
        {
            if (!full.StartsWith(Settings.BaseDir, StringComparison.OrdinalIgnoreCase)) return new Destination { Path = full };
            var relative = full.Substring(Settings.BaseDir.Length);
            return new Destination { Path = relative.Length > 0 ? relative : "." };
        }
    }

    /// <summary>Portable settings: DonatScreen.ini next to the exe.</summary>
    sealed class Settings
    {
        public static readonly string BaseDir = AppDomain.CurrentDomain.BaseDirectory;
        public static readonly string FilePath = Path.Combine(BaseDir, "DonatScreen.ini");

        public readonly List<Destination> Folders = new List<Destination>();
        public int DefaultIndex;
        public bool AskEachTime = true;
        public Keys RegionKey = Keys.Control | Keys.Shift | Keys.D2;
        public Keys FullKey = Keys.Control | Keys.Shift | Keys.D1;
        public string Lang = "en";  // "en" or "id"

        public Destination Default => Folders[DefaultIndex];

        public static Settings Load()
        {
            var s = new Settings();
            string[] lines;
            try { lines = File.ReadAllLines(FilePath); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { lines = new string[0]; }

            foreach (var line in lines)
            {
                int i = line.IndexOf('=');
                if (i < 0) continue;
                string key = line.Substring(0, i).Trim(), value = line.Substring(i + 1).Trim();
                switch (key)
                {
                    case "Folder": // v1.0 had a single folder
                        if (value.Length > 0) s.Folders.Add(new Destination { Path = value });
                        break;
                    case "Dest": // "1|path": visible flag, path
                        int bar = value.IndexOf('|');
                        if (bar > 0 && bar < value.Length - 1)
                            s.Folders.Add(new Destination { Path = value.Substring(bar + 1).Trim(), Visible = value.Substring(0, bar) != "0" });
                        break;
                    case "Default": int.TryParse(value, out s.DefaultIndex); break;
                    case "Ask": s.AskEachTime = value != "0"; break;
                    case "RegionKey": if (TryParseKeys(value, out var region)) s.RegionKey = region; break;
                    case "FullKey": if (TryParseKeys(value, out var full)) s.FullKey = full; break;
                    case "Lang": s.Lang = value == "id" ? "id" : "en"; break;
                }
            }
            if (s.Folders.Count == 0)
                s.Folders.Add(new Destination { Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Donat Screen") });
            if (s.DefaultIndex < 0 || s.DefaultIndex >= s.Folders.Count) s.DefaultIndex = 0;
            s.Default.Visible = true;
            return s;
        }

        public bool Save()
        {
            var lines = Folders.Select(f => $"Dest={(f.Visible ? 1 : 0)}|{f.Path}").Concat(new[]
            {
                "Default=" + DefaultIndex,
                "Ask=" + (AskEachTime ? 1 : 0),
                "RegionKey=" + KeysToString(RegionKey),
                "FullKey=" + KeysToString(FullKey),
                "Lang=" + Lang,
            });
            try
            {
                File.WriteAllLines(FilePath, lines);
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return false;
            }
        }

        // Keys has no Win flag, so it is written as an extra ", Win" token: "S, Shift, Win".
        static string KeysToString(Keys k) => (k & Hotkeys.Win) != 0 ? (k & ~Hotkeys.Win) + ", Win" : k.ToString();

        static bool TryParseKeys(string text, out Keys keys)
        {
            var parts = text.Split(',').Select(p => p.Trim()).ToList();
            bool win = parts.Remove("Win");
            bool ok = Enum.TryParse(string.Join(",", parts), out keys) && (keys & Keys.KeyCode) != Keys.None;
            if (win) keys |= Hotkeys.Win;
            return ok;
        }
    }

    /// <summary>Opt-in "start with Windows" via the per-user Run key (no admin needed).</summary>
    static class Autostart
    {
        public const string Flag = "--autostart";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", Name = "DonatScreen";

        public static bool Enabled
        {
            get => Registered != null;
            set
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                if (value) key.SetValue(Name, $"\"{Application.ExecutablePath}\" {Flag}");
                else key.DeleteValue(Name, false);
            }
        }

        static string Registered
        {
            get { using var key = Registry.CurrentUser.OpenSubKey(RunKey); return key?.GetValue(Name) as string; }
        }

        /// <summary>The exe is portable: if the registered copy no longer exists, point autostart at this one.</summary>
        public static void Repair()
        {
            var exe = Registered?.Split('"').ElementAtOrDefault(1);
            if (exe == null || File.Exists(exe)) return;
            try { Enabled = true; }
            catch (Exception e) when (e is UnauthorizedAccessException || e is System.Security.SecurityException) { }
        }
    }

    /// <summary>Message-only window that receives global hotkeys.</summary>
    sealed class Hotkeys : NativeWindow
    {
        /// <summary>Win modifier, stored in a bit <see cref="Keys"/> leaves unused (Shift/Control/Alt are 0x10000/0x20000/0x40000).</summary>
        public const Keys Win = (Keys)0x80000;

        public event Action<int> Pressed;

        public Hotkeys() => CreateHandle(new CreateParams { Parent = (IntPtr)(-3) }); // HWND_MESSAGE

        public bool Register(int id, Keys keys)
        {
            uint mods = 0x4000; // MOD_NOREPEAT
            if ((keys & Keys.Alt) != 0) mods |= 1;
            if ((keys & Keys.Control) != 0) mods |= 2;
            if ((keys & Keys.Shift) != 0) mods |= 4;
            if ((keys & Win) != 0) mods |= 8;
            return Native.RegisterHotKey(Handle, id, mods, (uint)(keys & Keys.KeyCode));
        }

        public void Unregister(int id) => Native.UnregisterHotKey(Handle, id);

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312) Pressed?.Invoke((int)m.WParam); // WM_HOTKEY
            base.WndProc(ref m);
        }

        public static string Format(Keys keys)
        {
            var parts = new List<string>();
            if ((keys & Win) != 0) parts.Add("Win");
            if ((keys & Keys.Control) != 0) parts.Add("Ctrl");
            if ((keys & Keys.Alt) != 0) parts.Add("Alt");
            if ((keys & Keys.Shift) != 0) parts.Add("Shift");
            var code = keys & Keys.KeyCode;
            uint ch = Native.MapVirtualKey((uint)code, 2) & 0xFFFF; // MAPVK_VK_TO_CHAR: what the key prints on this layout
            parts.Add(ch > 32 && ch < 127 ? ((char)ch).ToString() : code == Keys.PrintScreen ? "PrtSc" : code.ToString());
            return string.Join(" + ", parts);
        }

        // Windows shortcuts that RegisterHotKey may still hand out (handled by hooks, not registrations).
        // Win + digit and Win + arrow are blocked by rule in Conflict().
        static readonly Keys[] Reserved =
        {
            Keys.Alt | Keys.PrintScreen, Win | Keys.PrintScreen, Win | Keys.Alt | Keys.PrintScreen, Win | Keys.Shift | Keys.PrintScreen,
            Keys.Alt | Keys.Shift | Keys.Tab, Keys.Control | Keys.Alt | Keys.Tab, Keys.Control | Keys.Shift | Keys.Escape, Keys.Control | Keys.Alt | Keys.Delete,
            Win | Keys.Shift | Keys.S, Win | Keys.Shift | Keys.R, Win | Keys.Shift | Keys.M, Win | Keys.Shift | Keys.V,
            Win | Keys.Shift | Keys.C, Win | Keys.Shift | Keys.Space, Win | Keys.Shift | Keys.Enter,
            Win | Keys.Control | Keys.D, Win | Keys.Control | Keys.F4, Win | Keys.Control | Keys.Enter, Win | Keys.Control | Keys.C,
            Win | Keys.Control | Keys.O, Win | Keys.Control | Keys.F, Win | Keys.Control | Keys.Q, Win | Keys.Control | Keys.Space,
            Win | Keys.Control | Keys.V, Win | Keys.Control | Keys.N, Win | Keys.Control | Keys.S, Win | Keys.Control | Keys.M,
            Win | Keys.Control | Keys.Shift | Keys.B,
            Win | Keys.Alt | Keys.R, Win | Keys.Alt | Keys.G, Win | Keys.Alt | Keys.B, Win | Keys.Alt | Keys.D,
            Win | Keys.Alt | Keys.K, Win | Keys.Alt | Keys.H, Win | Keys.Alt | Keys.Enter,
        };

        /// <summary>Why a combination would clash with Windows or with everyday typing/app shortcuts; null if it is safe.</summary>
        public static string Conflict(Keys keys)
        {
            var code = keys & Keys.KeyCode;
            bool win = (keys & Win) != 0;
            int mods = new[] { Keys.Control, Keys.Alt, Keys.Shift }.Count(m => (keys & m) != 0);
            bool special = code >= Keys.F13 && code <= Keys.F24 || code == Keys.PrintScreen || code == Keys.Pause || code == Keys.Scroll;

            if (keys == Keys.PrintScreen && SnippingOwnsPrintScreen())
                return L.T("PrtSc dipakai Snipping Tool. Matikan di Pengaturan Windows › Aksesibilitas › Keyboard, atau pilih kombinasi lain.");
            if (Reserved.Contains(keys) || win && (code >= Keys.D0 && code <= Keys.D9 || code >= Keys.Left && code <= Keys.Down))
                return L.T("{0} adalah shortcut bawaan Windows.", Format(keys));
            if (win && mods == 0)
                return L.T("Win + satu tombol dipakai Windows. Tambahkan Ctrl, Alt, atau Shift.");
            if (!win && mods < 2 && !special)
                return L.T("Pakai minimal dua dari Ctrl, Alt, Shift (atau Win) agar tidak bentrok dengan shortcut aplikasi.");
            return null;
        }

        static bool SnippingOwnsPrintScreen() =>
            !(Registry.GetValue(@"HKEY_CURRENT_USER\Control Panel\Keyboard", "PrintScreenKeyForSnippingEnabled", 1) is int on && on == 0);
    }

    static class Theme
    {
        public static readonly bool Dark = Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int light && light == 0;

        public static readonly Color Back = Dark ? Color.FromArgb(28, 28, 28) : Color.FromArgb(243, 243, 243);
        public static readonly Color Surface = Dark ? Color.FromArgb(44, 44, 44) : Color.White;
        public static readonly Color Hover = Dark ? Color.FromArgb(60, 60, 60) : Color.FromArgb(232, 232, 232);
        public static readonly Color Border = Dark ? Color.FromArgb(92, 92, 92) : Color.FromArgb(196, 196, 196);
        public static readonly Color Text = Dark ? Color.FromArgb(250, 250, 250) : Color.FromArgb(18, 18, 18);
        public static readonly Color Muted = Dark ? Color.FromArgb(196, 196, 196) : Color.FromArgb(84, 84, 84);
        // Red from the icon's family, one shade deeper than its glaze (#EF5350) so white text on it stays readable (≈5:1).
        public static readonly Color Accent = Color.FromArgb(211, 47, 47);
        public static readonly Color AccentHover = Color.FromArgb(229, 57, 53);
        public static readonly Color Error = Dark ? Color.FromArgb(255, 110, 120) : Color.FromArgb(200, 30, 45);

        public static readonly Font Normal = new Font("Segoe UI", 10f);
        public static readonly Font Small = new Font("Segoe UI", 9f);
        public static readonly Font Caption = new Font("Segoe UI Semibold", 9.5f);
        public static readonly Font Symbols = LoadSymbols(11f);
        public static readonly Icon AppIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        // Windows icon font glyphs (same code points in Segoe Fluent Icons and Segoe MDL2 Assets).
        public const string Add = "", Close = "", Star = "", StarFill = "", OpenFolder = "";

        /// <summary>Segoe Fluent Icons on Windows 11, Segoe MDL2 Assets on Windows 10.</summary>
        static Font LoadSymbols(float size)
        {
            var fluent = new Font("Segoe Fluent Icons", size);
            if (fluent.Name == "Segoe Fluent Icons") return fluent;
            fluent.Dispose();
            return new Font("Segoe MDL2 Assets", size);
        }

        /// <summary>A path on one line in muted text: middle shortened, or end-cut when the last folder alone is too long.</summary>
        public static void DrawPath(Graphics g, string path, Rectangle r, TextFormatFlags align)
        {
            bool lastFits = TextRenderer.MeasureText(Path.GetFileName(path.TrimEnd('\\')), Small).Width < r.Width * 3 / 4;
            // No NoPadding here: with it GDI skips the ellipsis when the text overflows only slightly.
            // Shift left by the default glyph padding (1/6 em) so the path still lines up with the name above.
            int overhang = (int)Math.Ceiling(Small.Height / 6f);
            TextRenderer.DrawText(g, path, Small, Rectangle.FromLTRB(r.Left - overhang, r.Top, r.Right, r.Bottom), Muted,
                align | TextFormatFlags.Left | TextFormatFlags.SingleLine | (lastFits ? TextFormatFlags.PathEllipsis : TextFormatFlags.EndEllipsis));
        }

        /// <summary>An icon glyph as a bitmap, for placing next to button text.</summary>
        public static Bitmap Glyph(string glyph, Color color, int px)
        {
            var bmp = new Bitmap(px, px);
            using var g = Graphics.FromImage(bmp);
            using var font = new Font(Symbols.FontFamily, px * 0.8f, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(color);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.DrawString(glyph, font, brush, new RectangleF(0, 0, px, px), new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
            return bmp;
        }

        public static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    sealed class FlatRenderer : ToolStripProfessionalRenderer
    {
        public FlatRenderer() : base(new Colors()) => RoundedEdges = false;

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected) return;
            using var b = new SolidBrush(Theme.Hover);
            e.Graphics.FillRectangle(b, 4, 0, e.Item.Width - 8, e.Item.Height);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Text == (e.Item as ToolStripMenuItem)?.ShortcutKeyDisplayString ? Theme.Muted : Theme.Text;
            base.OnRenderItemText(e);
        }

        sealed class Colors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => Theme.Surface;
            public override Color MenuBorder => Theme.Border;
            public override Color SeparatorDark => Theme.Border;
            public override Color SeparatorLight => Theme.Surface;
        }
    }

    static class Native
    {
        public delegate IntPtr HookProc(int code, IntPtr message, IntPtr info);

        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint mapType);
        [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int processId);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
        [DllImport("user32.dll")] public static extern IntPtr SetWindowsHookEx(int type, HookProc proc, IntPtr module, uint threadId);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr info);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string name);
        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int srcX, int srcY, uint rop);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] public static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern uint ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[] small, uint count);
    }
}
