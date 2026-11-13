using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace DonatScreen
{
    /// <summary>Frozen full-desktop snapshot; drag to pick an area, click for one monitor, Esc / right-click to cancel.</summary>
    sealed class RegionOverlay : Form
    {
        static string HintText => L.T("Seret untuk memilih area   ·   Klik untuk satu layar   ·   Esc batal");

        readonly Bitmap shot, dim;
        readonly Rectangle hint;
        Point start;
        Rectangle sel, painted;
        bool dragging;

        public Bitmap Result { get; private set; }

        public RegionOverlay(Bitmap shot)
        {
            this.shot = shot;
            dim = shot.Clone(new Rectangle(Point.Empty, shot.Size), PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(dim))
            using (var b = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
                g.FillRectangle(b, 0, 0, dim.Width, dim.Height);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = SystemInformation.VirtualScreen;
            TopMost = true;
            ShowInTaskbar = false;
            Cursor = Cursors.Cross;
            DoubleBuffered = true;
            Font = new Font("Segoe UI", 10f);

            var screen = Screen.FromPoint(MousePosition).Bounds;
            var size = TextRenderer.MeasureText(HintText, Font) + new Size(36, 18);
            hint = new Rectangle(screen.X - Left + (screen.Width - size.Width) / 2, screen.Y - Top + 32, size.Width, size.Height);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var clip = e.ClipRectangle;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.DrawImage(dim, clip, clip, GraphicsUnit.Pixel);
            if (!sel.IsEmpty)
            {
                var lit = Rectangle.Intersect(sel, clip);
                if (!lit.IsEmpty) g.DrawImage(shot, lit, lit, GraphicsUnit.Pixel);
                using (var pen = new Pen(Theme.Accent, 2)) g.DrawRectangle(pen, sel);
                Pill(g, $"{sel.Width} × {sel.Height}", SizeLabel());
            }
            else if (!dragging)
            {
                Pill(g, HintText, hint);
            }
        }

        void Pill(Graphics g, string text, Rectangle r)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Rounded(r, r.Height / 2))
            using (var b = new SolidBrush(Color.FromArgb(225, 24, 24, 24)))
                g.FillPath(b, path);
            g.SmoothingMode = SmoothingMode.None;
            TextRenderer.DrawText(g, text, Font, r, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        Rectangle SizeLabel()
        {
            var size = TextRenderer.MeasureText($"{sel.Width} × {sel.Height}", Font) + new Size(20, 10);
            int y = sel.Bottom + 8 + size.Height <= ClientSize.Height ? sel.Bottom + 8 : sel.Y - size.Height - 8;
            return new Rectangle(new Point(sel.X, Math.Max(0, y)), size);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) { Close(); return; }
            if (e.Button != MouseButtons.Left) return;
            dragging = true;
            start = e.Location;
            Invalidate(hint);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!dragging) return;
            sel = Rectangle.FromLTRB(Math.Min(start.X, e.X), Math.Min(start.Y, e.Y), Math.Max(start.X, e.X) + 1, Math.Max(start.Y, e.Y) + 1);
            sel.Intersect(new Rectangle(Point.Empty, shot.Size));
            // Repaint only what changed: old + new selection and their size labels.
            var area = Rectangle.Union(sel, SizeLabel());
            area.Inflate(4, 4);
            Invalidate(Rectangle.Union(painted, area));
            painted = area;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!dragging || e.Button != MouseButtons.Left) return;
            dragging = false;
            if (sel.Width < 4 || sel.Height < 4)
            {
                sel = Screen.FromPoint(PointToScreen(e.Location)).Bounds;
                sel.Offset(-Left, -Top);
                sel.Intersect(new Rectangle(Point.Empty, shot.Size));
            }
            Result = shot.Clone(sel, shot.PixelFormat);
            DialogResult = DialogResult.OK;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) dim.Dispose();
            base.Dispose(disposing);
        }
    }
}
