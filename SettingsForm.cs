using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows.Forms;

namespace DonatScreen
{
    sealed class SettingsForm : ThemedForm
    {
        static string Hint => L.T("Klik kotak shortcut, lalu tekan kombinasi baru. Bisa pakai Ctrl, Alt, Shift, dan Win.");
        static string SaveError => L.T("Tidak bisa menulis DonatScreen.ini di folder aplikasi.");
        static string Duplicate => L.T("Folder itu sudah ada di daftar.");

        readonly App app;
        Label status;
        Panel list;
        int listScroll; // kept across rebuilds so editing row 7 doesn't jump back to the top

        Settings S => app.Settings;

        public SettingsForm(App app)
        {
            this.app = app;
            StartPosition = FormStartPosition.CenterScreen;
            Build(null);
        }

        /// <summary>Lays out the whole window; called again whenever the folder list changes size.</summary>
        void Build(string message)
        {
            SuspendLayout();
            if (list != null && listScroll != int.MaxValue) listScroll = -list.AutoScrollPosition.Y;
            foreach (var c in Controls.Cast<Control>().ToArray()) c.Dispose();
            Tips.RemoveAll();

            int y = 16;
            Caption(L.T("Folder tujuan"), y);
            y += 22;
            int rows = S.Folders.Count;
            list = ScrollList(y, rows, 42, 2, Theme.Surface);
            list.Paint += Outline;
            for (int i = 0; i < rows; i++) FolderRow(i, 1 + 42 * i, RowWidth(rows, 392));
            y += Math.Min(rows, VisibleRows) * 42 + 2 + 8;
            var add = Put(FlatButton(L.T("Tambah folder"), AddFolder), 24, y, 140, 32);
            add.Image = Theme.Glyph(Theme.Add, Theme.Text, K(14));
            add.ImageAlign = ContentAlignment.MiddleLeft;
            add.TextImageRelation = TextImageRelation.ImageBeforeText;
            add.Padding = new Padding(K(8), 0, K(8), 0);
            Note(L.T("Centang = tampil di popup  ·  ★ = default"), 172, y, 244, 32, ContentAlignment.MiddleRight);
            y += 46;

            Caption(L.T("Setelah screenshot"), y);
            y += 22;
            var ask = Put(new Toggle { Text = L.T("Tanya folder dan nama file"), Checked = S.AskEachTime }, 24, y, 392, 28);
            ask.CheckedChanged += (o, e) => { S.AskEachTime = ask.Checked; Persist(); };
            y += 28;
            Note(L.T("Jika mati, langsung disimpan ke folder ★ dengan nama otomatis."), 24, y, 392, 18);
            y += 30;

            Caption(L.T("Shortcut"), y);
            y += 22;
            string regionName = L.T("Ambil area"), fullName = L.T("Ambil layar penuh");
            var region = Row(regionName, y, S.RegionKey);
            var full = Row(fullName, y + 38, S.FullKey);
            region.Accept = k => Accept(k, full, fullName, () => S.RegionKey = k);
            full.Accept = k => Accept(k, region, regionName, () => S.FullKey = k);
            y += 84;

            Caption(L.T("Umum"), y);
            y += 22;
            var autostart = Put(new Toggle { Text = L.T("Jalankan saat Windows menyala"), Checked = Autostart.Enabled }, 24, y, 392, 28);
            autostart.CheckedChanged += (o, e) => SetAutostart(autostart);
            y += 38;
            // Always bilingual, so it can be found whichever language is showing.
            Put(new Label { Text = "Language / Bahasa", TextAlign = ContentAlignment.MiddleLeft }, 24, y, 180, 32);
            LanguageButton("English", "en", 216, y);
            LanguageButton("Indonesia", "id", 318, y);
            y += 44;

            status = Put(new Label { Font = Theme.Small }, 24, y, 392, 36);
            ShowStatus(message);
            ClientSize = new Size(K(440), K(y + 40));
            ResumeLayout();
            list.AutoScrollPosition = new Point(0, listScroll); // clamped to the end when larger
            listScroll = 0;
        }

        void LanguageButton(string name, string code, int x, int y) =>
            Put(FlatButton(name, () => SetLanguage(code), primary: S.Lang == code), x, y, 98, 32);

        void SetLanguage(string code)
        {
            if (S.Lang == code) return;
            S.Lang = code;
            app.ApplyLanguage();
            Rebuild(); // saves, then lays the window out again in the new language
        }

        void FolderRow(int index, int y, int width)
        {
            var d = S.Folders[index];
            bool isDefault = index == S.DefaultIndex;
            int buttons = width - 102; // three 32-px icon buttons + gaps on the right

            var check = Put(list, new FolderCheck { Text = d.Name, Detail = d.FullPath, Checked = d.Visible }, 1, y, buttons - 3, 42);
            check.CheckedChanged += (o, e) =>
            {
                if (isDefault && !check.Checked)
                {
                    check.Checked = true;
                    ShowStatus(L.T("Folder default selalu muncul di pilihan."));
                    return;
                }
                d.Visible = check.Checked;
                Persist();
            };
            Tips.SetToolTip(check, d.Name + "\n" + d.FullPath + "\n" + L.T("Klik path untuk mengubah lokasi")); // narrow row: full name + path on hover

            // The row draws a shortened path; clicking that line opens an editor over it: type or paste a location,
            // Enter / click away applies, Esc reverts.
            var path = new TextBox
            {
                Bounds = new Rectangle(check.Left + check.TextLeft, check.Top + check.Height / 2 + K(2), check.Width - check.TextLeft - K(4), K(16)),
                Text = d.FullPath, BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.Text, Font = Theme.Small, Visible = false,
            };
            list.Controls.Add(path);
            path.BringToFront();
            check.PathClicked += () => { path.Visible = true; path.Focus(); path.SelectAll(); };
            path.Leave += (o, e) =>
            {
                CommitPath(d, path); // on success the window is rebuilt; otherwise back to the shortened view
                path.Visible = false;
            };
            path.KeyDown += (o, e) =>
            {
                if (e.KeyCode != Keys.Enter && e.KeyCode != Keys.Escape) return;
                if (e.KeyCode == Keys.Escape) path.Text = d.FullPath;
                else CommitPath(d, path);
                e.SuppressKeyPress = true;
                ActiveControl = null;
            };

            // Three equal 32-px icon buttons: open, default, remove.
            var open = Put(list, IconButton(Theme.OpenFolder, Theme.Muted, () => app.OpenFolder(d.FullPath)), buttons, y + 5, 32, 32);
            Tips.SetToolTip(open, L.T("Buka folder"));
            var star = Put(list, IconButton(isDefault ? Theme.StarFill : Theme.Star, isDefault ? Theme.Accent : Theme.Muted, () => SetDefault(index)), buttons + 34, y + 5, 32, 32);
            Tips.SetToolTip(star, isDefault ? L.T("Folder default") : L.T("Jadikan default"));
            var remove = Put(list, IconButton(Theme.Close, Theme.Muted, () => Remove(index)), buttons + 68, y + 5, 32, 32);
            remove.Enabled = S.Folders.Count > 1;
            Tips.SetToolTip(remove, L.T("Hapus dari daftar (isi folder tidak dihapus)"));
        }

        HotkeyBox Row(string label, int y, Keys value)
        {
            Put(new Label { Text = label, TextAlign = ContentAlignment.MiddleLeft }, 24, y, 180, 32);
            // Text is never painted; it names the box for screen readers / UI Automation.
            var box = Put(new HotkeyBox { Text = label, Value = value }, 216, y, 200, 32);
            // Hotkeys are released while recording, otherwise pressing the current one would fire a capture.
            box.Enter += (o, e) => { app.SuspendHotkeys(); ShowStatus(null); };
            box.Leave += (o, e) => ShowStatus(app.ApplyHotkeys());
            return box;
        }

        void AddFolder()
        {
            var path = FolderPicker.Show(Handle, S.Default.FullPath);
            if (path == null) return;
            if (Listed(path, null))
            {
                ShowStatus(Duplicate);
                return;
            }
            S.Folders.Add(Destination.FromFullPath(path));
            listScroll = int.MaxValue; // show the new row at the bottom
            Rebuild();
        }

        bool Listed(string fullPath, Destination except) =>
            S.Folders.Any(f => f != except && string.Equals(f.FullPath.TrimEnd('\\'), fullPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));

        /// <summary>Repoints a folder at an edited path (relative paths resolve against the exe folder); refuses missing or duplicate folders.</summary>
        void CommitPath(Destination d, TextBox box)
        {
            var text = box.Text.Trim().Trim('"');
            if (text.Length == 0 || text == d.FullPath)
            {
                box.Text = d.FullPath;
                return;
            }
            string full = null;
            try { full = Path.GetFullPath(Path.Combine(Settings.BaseDir, text)); }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException) { }
            var error = full == null || !Directory.Exists(full) ? L.T("Folder tidak ditemukan: {0}", text)
                : Listed(full, d) ? Duplicate : null;
            if (error != null)
            {
                box.Text = d.FullPath;
                ShowStatus(error);
                return;
            }
            d.Path = Destination.FromFullPath(full).Path;
            Rebuild();
        }

        void SetDefault(int index)
        {
            S.DefaultIndex = index;
            S.Default.Visible = true;
            Rebuild();
        }

        void Remove(int index)
        {
            if (S.Folders.Count <= 1) return;
            S.Folders.RemoveAt(index);
            if (S.DefaultIndex == index) S.DefaultIndex = 0;
            else if (S.DefaultIndex > index) S.DefaultIndex--;
            S.Default.Visible = true;
            Rebuild();
        }

        /// <summary>Saves, then relayouts after the click that triggered it has finished (it disposes the clicked button).</summary>
        void Rebuild()
        {
            var message = S.Save() ? null : SaveError;
            BeginInvoke(new Action(() => { if (!IsDisposed) Build(message); }));
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            ActiveControl = null; // flush a path edit still in progress
            base.OnFormClosing(e);
        }

        /// <summary>Takes a recorded combination only if nothing else owns it: the other action, Windows, or another app.</summary>
        bool Accept(Keys keys, HotkeyBox other, string otherName, Action store)
        {
            var error = keys == other.Value ? L.T("{0} sudah dipakai untuk {1}.", Hotkeys.Format(keys), otherName)
                : Hotkeys.Conflict(keys)
                ?? (app.CanRegister(keys) ? null : L.T("{0} sudah dipakai Windows atau aplikasi lain.", Hotkeys.Format(keys)));
            if (error != null)
            {
                ShowStatus(error);
                return false;
            }
            store();
            Persist();
            return true;
        }

        void SetAutostart(Toggle toggle)
        {
            try
            {
                Autostart.Enabled = toggle.Checked;
                ShowStatus(null);
            }
            catch (Exception e) when (e is UnauthorizedAccessException || e is SecurityException || e is IOException)
            {
                ShowStatus(L.T("Autostart tidak bisa diubah: {0}", e.Message));
            }
        }

        void Persist()
        {
            if (!S.Save()) ShowStatus(SaveError);
        }

        void ShowStatus(string error)
        {
            status.Text = error ?? Hint;
            status.ForeColor = error == null ? Theme.Muted : Theme.Error;
        }
    }

    /// <summary>
    /// Click, then press a key combination. While focused, a low-level keyboard hook records and swallows every key,
    /// so Win combos are seen too and Win+E etc. don't fire mid-recording.
    /// </summary>
    sealed class HotkeyBox : Control
    {
        Keys value;
        IntPtr hook;
        readonly Native.HookProc proc;  // held in a field: Windows calls it, the GC must not collect it
        Keys held;                      // modifiers we swallowed while recording (they never reach GetKeyState)

        public Func<Keys, bool> Accept;

        public Keys Value
        {
            get => value;
            set { this.value = value; Invalidate(); }
        }

        public HotkeyBox()
        {
            SetStyle(ControlStyles.Selectable | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            BackColor = Theme.Surface;
            proc = OnKey;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            base.OnMouseDown(e);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            if (hook == IntPtr.Zero) hook = Native.SetWindowsHookEx(13, proc, Native.GetModuleHandle(null), 0); // WH_KEYBOARD_LL
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            // Keep swallowing until the modifiers we ate are released, else Windows sees a lone Win key-up and opens Start.
            if (held == Keys.None) Unhook();
            Invalidate();
            base.OnLostFocus(e);
        }

        protected override void Dispose(bool disposing)
        {
            Unhook();
            base.Dispose(disposing);
        }

        void Unhook()
        {
            if (hook != IntPtr.Zero) Native.UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
        }

        IntPtr OnKey(int code, IntPtr message, IntPtr info)
        {
            if (code < 0) return Native.CallNextHookEx(hook, code, message, info);
            var vk = (Keys)Marshal.ReadInt32(info);
            bool down = message == (IntPtr)0x100 || message == (IntPtr)0x104; // WM_KEYDOWN, WM_SYSKEYDOWN
            var mod = ModifierOf(vk);

            if (mod != Keys.None)
            {
                if (down && Focused) held |= mod;
                else if (!down && (held & mod) != 0) held &= ~mod;
                else return Native.CallNextHookEx(hook, code, message, info); // not ours: pressed before recording began
                if (!Focused && held == Keys.None) BeginInvoke(new Action(Unhook));
                return (IntPtr)1;
            }
            if (!Focused || held == Keys.None && vk == Keys.Tab)
                return Native.CallNextHookEx(hook, code, message, info); // Tab still moves focus
            if (down)
            {
                var keys = vk | held;
                BeginInvoke(new Action(() => Record(keys)));
            }
            return (IntPtr)1;
        }

        static Keys ModifierOf(Keys vk)
        {
            switch (vk)
            {
                case Keys.LControlKey: case Keys.RControlKey: case Keys.ControlKey: return Keys.Control;
                case Keys.LShiftKey: case Keys.RShiftKey: case Keys.ShiftKey: return Keys.Shift;
                case Keys.LMenu: case Keys.RMenu: case Keys.Menu: return Keys.Alt;
                case Keys.LWin: case Keys.RWin: return Hotkeys.Win;
                default: return Keys.None;
            }
        }

        void Record(Keys keys)
        {
            if (!Focused) return;
            if (keys == Keys.Escape)
            {
                FindForm().ActiveControl = null;
                return;
            }
            if (Accept != null && !Accept(keys)) return; // refused: stay in recording mode, status explains why
            Value = keys;
            FindForm().ActiveControl = null;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            TextRenderer.DrawText(g, Focused ? L.T("Tekan shortcut…") : Hotkeys.Format(value), Font, ClientRectangle,
                Focused ? Theme.Muted : ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            ControlPaint.DrawBorder(g, ClientRectangle, Focused ? Theme.Accent : Theme.Border, ButtonBorderStyle.Solid);
        }
    }
}
