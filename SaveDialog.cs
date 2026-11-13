using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace DonatScreen
{
    /// <summary>
    /// Shown after a capture when "ask" is on: preview, file name, a checkbox per offered folder (default pre-ticked).
    /// Simpan / Enter saves into every ticked folder; Esc / close keeps the default folder and the automatic name.
    /// </summary>
    sealed class SaveDialog : ThemedForm
    {
        readonly List<(Destination folder, FolderCheck box)> rows = new List<(Destination, FolderCheck)>();
        readonly TextBox name;
        readonly Label error;
        readonly string autoName;

        public List<string> Folders { get; private set; }
        public string FileName { get; private set; }

        public SaveDialog(Bitmap shot, Settings settings, string autoName)
        {
            this.autoName = autoName;
            Folders = new List<string> { settings.Default.FullPath };
            FileName = autoName;

            Text = L.T("Simpan screenshot");
            TopMost = true;
            KeyPreview = true;
            StartPosition = FormStartPosition.Manual;

            var preview = Put(new PictureBox { Image = shot, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Surface }, 24, 16, 392, 176);
            preview.Paint += Outline;

            Caption(L.T("Nama file"), 204);
            var field = Put(new Panel { BackColor = Theme.Surface }, 24, 226, 392, 32);
            field.Paint += Outline;
            name = Put(field, new TextBox { Text = autoName, BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.Text }, 10, 7, 330, 20);
            Put(field, new Label { Text = ".png", ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleRight }, 340, 1, 44, 30);
            error = Put(new Label { ForeColor = Theme.Error, Font = Theme.Small }, 24, 260, 392, 18);

            Caption(L.T("Simpan ke  —  centang satu atau beberapa folder"), 280);
            var offered = settings.Folders.Where(f => f.Visible).ToList();
            var list = ScrollList(302, offered.Count, 46, -4, Theme.Back);
            int width = RowWidth(offered.Count, 392);
            for (int i = 0; i < offered.Count; i++)
            {
                var d = offered[i];
                bool isDefault = d == settings.Default;
                var box = Put(list, new FolderCheck { Text = d.Name, Detail = d.FullPath, Star = isDefault, Bordered = true, Checked = isDefault }, 0, 46 * i, width, 42);
                box.CheckedChanged += (o, e) => error.Text = "";
                rows.Add((d, box));
            }

            int y = 302 + Math.Min(offered.Count, VisibleRows) * 46;
            Note(L.T("Enter simpan   ·   Esc simpan ke ★"), 24, y + 6, 280, 32);
            AcceptButton = Put(FlatButton(L.T("Simpan"), Save, primary: true), 312, y + 6, 104, 32);
            ClientSize = new Size(K(440), K(y + 54));

            var area = Screen.FromPoint(MousePosition).WorkingArea;
            Location = new Point(area.X + (area.Width - Width) / 2, area.Y + (area.Height - Height) / 2);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            name.Focus();
            name.SelectAll();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) Close();
            base.OnKeyDown(e);
        }

        void Save()
        {
            var n = name.Text.Trim();
            if (n.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 4);
            n = n.TrimEnd('.', ' '); // Windows drops these silently
            if (n.Length == 0) n = autoName;
            if (n.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Regex.IsMatch(n, @"^(CON|PRN|AUX|NUL|COM\d|LPT\d)(\..*)?$", RegexOptions.IgnoreCase))
            {
                error.Text = L.T("Nama tidak boleh memuat \\ / : * ? \" < > | atau nama khusus Windows (CON, NUL, …).");
                name.Focus();
                return;
            }
            var ticked = rows.Where(r => r.box.Checked).Select(r => r.folder.FullPath).ToList();
            if (ticked.Count == 0)
            {
                error.Text = L.T("Centang minimal satu folder tujuan.");
                return;
            }
            Folders = ticked;
            FileName = n;
            DialogResult = DialogResult.OK;
        }
    }
}
