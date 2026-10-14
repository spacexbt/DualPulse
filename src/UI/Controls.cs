using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DualPulse
{
    public static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(12, 13, 18);
        public static readonly Color Side = Color.FromArgb(17, 18, 25);
        public static readonly Color Card = Color.FromArgb(22, 24, 33);
        public static readonly Color Border = Color.FromArgb(38, 41, 54);
        public static readonly Color Track = Color.FromArgb(42, 45, 60);
        public static readonly Color Text = Color.FromArgb(236, 238, 245);
        public static readonly Color Muted = Color.FromArgb(132, 138, 158);
        public static readonly Color A1 = Color.FromArgb(124, 92, 255);
        public static readonly Color A2 = Color.FromArgb(0, 212, 255);
        public static readonly Color Ok = Color.FromArgb(46, 213, 115);
        public static readonly Color Bad = Color.FromArgb(255, 82, 82);
        public static Font F(float size, FontStyle st = FontStyle.Regular) { return new Font("Segoe UI", size, st); }
        public static readonly Font Body = F(9.5f), Small = F(8.5f), Bold = F(9.5f, FontStyle.Bold), H1 = F(16f, FontStyle.Bold), H2 = F(11f, FontStyle.Bold);

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }

        public static Brush Accent(RectangleF r)
        {
            if (r.Width < 1) r.Width = 1;
            return new LinearGradientBrush(new PointF(r.Left - 1, 0), new PointF(r.Right + 1, 0), A1, A2);
        }

        public static void Smooth(Graphics g) { g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit; }
    }

    public class DControl : Control
    {
        public DControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Theme.Card; ForeColor = Theme.Text; Font = Theme.Body;
        }
        protected float S { get { return DeviceDpiScale(); } }
        float DeviceDpiScale() { using (var g = CreateGraphics()) return g.DpiX / 96f; }
    }

    public class Card : Panel
    {
        public string Title;
        public Card(string title)
        {
            Title = title; BackColor = Theme.Bg; ForeColor = Theme.Text;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Smooth(g);
            g.Clear(Theme.Bg);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var p = Theme.Round(r, 12)) { using (var b = new SolidBrush(Theme.Card)) g.FillPath(b, p); using (var pen = new Pen(Theme.Border)) g.DrawPath(pen, p); }
            if (!string.IsNullOrEmpty(Title))
                TextRenderer.DrawText(g, Title.ToUpperInvariant(), Theme.F(8.5f, FontStyle.Bold), new Point((int)(16 * g.DpiX / 96), (int)(12 * g.DpiX / 96)), Theme.Muted, TextFormatFlags.NoPrefix);
        }
    }

    public class Label2 : Label
    {
        public Label2(string text, Font f, Color c) { Text = text; Font = f; ForeColor = c; BackColor = Theme.Card; AutoSize = true; }
    }

    public class Slider : DControl
    {
        int val, min, max = 100, step = 1;
        public Func<int, string> Format = v => v.ToString();
        public string Caption = "";
        public event EventHandler ValueChanged, Committed;
        bool drag;
        public int Minimum { get { return min; } set { min = value; Invalidate(); } }
        public int Maximum { get { return max; } set { max = value; Invalidate(); } }
        public int Step { get { return step; } set { step = Math.Max(1, value); } }
        public int Value
        {
            get { return val; }
            set
            {
                int v = Math.Max(min, Math.Min(max, min + (int)Math.Round((value - min) / (double)step) * step));
                if (v != val) { val = v; Invalidate(); if (ValueChanged != null) ValueChanged(this, EventArgs.Empty); }
            }
        }
        public Slider() { TabStop = true; Height = 50; }

        RectangleF TrackRect { get { float s = S; return new RectangleF(8 * s, Height - 16 * s, Width - 16 * s, 6 * s); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Smooth(g); g.Clear(BackColor);
            float s = S;
            TextRenderer.DrawText(g, Caption, Font, new Point((int)(6 * s), 0), Theme.Text);
            string vs = Format(val);
            var sz = TextRenderer.MeasureText(vs, Theme.Bold);
            TextRenderer.DrawText(g, vs, Theme.Bold, new Point(Width - sz.Width - (int)(6 * s), 0), Theme.A2);
            var t = TrackRect;
            using (var p = Theme.Round(t, t.Height / 2)) using (var b = new SolidBrush(Theme.Track)) g.FillPath(b, p);
            float k = max == min ? 0 : (val - min) / (float)(max - min);
            var f = new RectangleF(t.X, t.Y, Math.Max(t.Height, t.Width * k), t.Height);
            using (var p = Theme.Round(f, t.Height / 2)) using (var b = Theme.Accent(t)) g.FillPath(b, p);
            float cx = t.X + t.Width * k, cy = t.Y + t.Height / 2, rr = (Focused || drag ? 9 : 8) * s;
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, cx - rr, cy - rr, rr * 2, rr * 2);
            using (var pen = new Pen(Theme.A1, 2 * s)) g.DrawEllipse(pen, cx - rr, cy - rr, rr * 2, rr * 2);
        }

        void SetFromX(int x) { var t = TrackRect; Value = min + (int)Math.Round((x - t.X) / t.Width * (max - min)); }
        protected override void OnMouseDown(MouseEventArgs e) { Focus(); drag = true; SetFromX(e.X); }
        protected override void OnMouseMove(MouseEventArgs e) { if (drag) SetFromX(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { if (drag) { drag = false; Invalidate(); if (Committed != null) Committed(this, EventArgs.Empty); } }
        protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left) Value -= step; else if (e.KeyCode == Keys.Right) Value += step; else return;
            if (Committed != null) Committed(this, EventArgs.Empty);
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); }
    }

    public class Toggle : DControl
    {
        bool on;
        public event EventHandler Changed;
        public string Hint;
        public bool On { get { return on; } set { if (on != value) { on = value; Invalidate(); if (Changed != null) Changed(this, EventArgs.Empty); } } }
        public Toggle(string text) { Text = text; Height = 34; Cursor = Cursors.Hand; TabStop = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Smooth(g); g.Clear(BackColor);
            float s = S;
            var r = new RectangleF(Width - 46 * s, (Height - 24 * s) / 2, 44 * s, 24 * s);
            using (var p = Theme.Round(r, r.Height / 2))
            {
                if (on) using (var b = Theme.Accent(r)) g.FillPath(b, p);
                else using (var b = new SolidBrush(Theme.Track)) g.FillPath(b, p);
            }
            float d = r.Height - 6 * s;
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, on ? r.Right - d - 3 * s : r.X + 3 * s, r.Y + 3 * s, d, d);
            if (string.IsNullOrEmpty(Hint))
                TextRenderer.DrawText(g, Text, Font, new Rectangle((int)(6 * s), 0, (int)(Width - 60 * s), Height), Theme.Text, TextFormatFlags.VerticalCenter);
            else
            {
                TextRenderer.DrawText(g, Text, Font, new Point((int)(6 * s), (int)(Height / 2 - 18 * s)), Theme.Text);
                TextRenderer.DrawText(g, Hint, Theme.Small, new Point((int)(6 * s), (int)(Height / 2 + 1 * s)), Theme.Muted);
            }
            if (Focused) using (var pen = new Pen(Theme.Border) { DashStyle = DashStyle.Dot }) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
        protected override void OnClick(EventArgs e) { Focus(); On = !On; }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) On = !On; }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); }
    }

    public class Segmented : DControl
    {
        public string[] Items;
        int sel = -1, hover = -1;
        public event EventHandler Changed;
        public int Selected { get { return sel; } set { if (sel != value) { sel = value; Invalidate(); if (Changed != null) Changed(this, EventArgs.Empty); } } }
        public Segmented(params string[] items) { Items = items; Height = 36; Cursor = Cursors.Hand; TabStop = true; }
        Rectangle Seg(int i) { int w = Width / Items.Length; return new Rectangle(i * w, 0, i == Items.Length - 1 ? Width - i * w : w, Height); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Smooth(g); g.Clear(BackColor);
            float s = S;
            var all = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var p = Theme.Round(all, 8 * s)) using (var b = new SolidBrush(Theme.Bg)) g.FillPath(b, p);
            for (int i = 0; i < Items.Length; i++)
            {
                var r = Seg(i); var rf = new RectangleF(r.X + 3 * s, r.Y + 3 * s, r.Width - 6 * s, r.Height - 6 * s);
                if (i == sel) using (var p = Theme.Round(rf, 6 * s)) using (var b = Theme.Accent(rf)) g.FillPath(b, p);
                else if (i == hover) using (var p = Theme.Round(rf, 6 * s)) using (var b = new SolidBrush(Theme.Track)) g.FillPath(b, p);
                TextRenderer.DrawText(g, Items[i], i == sel ? Theme.Bold : Font, r, i == sel ? Color.White : Theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
            if (Focused) using (var pen = new Pen(Theme.A2) { DashStyle = DashStyle.Dot }) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
        protected override void OnMouseMove(MouseEventArgs e) { int h = e.X * Items.Length / Math.Max(1, Width); if (h != hover) { hover = h; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { Focus(); Selected = Math.Min(Items.Length - 1, e.X * Items.Length / Math.Max(1, Width)); }
        protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left && sel > 0) Selected = sel - 1;
            if (e.KeyCode == Keys.Right && sel < Items.Length - 1) Selected = sel + 1;
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); }
    }

    public class Swatch : DControl
    {
        Color color;
        public event EventHandler Changed;
        public Color Value { get { return color; } set { color = value; Invalidate(); } }
        public Swatch(string label) { Text = label; Height = 44; Cursor = Cursors.Hand; TabStop = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Smooth(g); g.Clear(BackColor);
            float s = S;
            var r = new RectangleF(2 * s, 4 * s, 36 * s, Height - 8 * s);
            using (var p = Theme.Round(r, 8 * s)) { using (var b = new SolidBrush(color)) g.FillPath(b, p); using (var pen = new Pen(Theme.Border, 2)) g.DrawPath(pen, p); }
            TextRenderer.DrawText(g, Text, Font, new Point((int)(46 * s), (int)(4 * s)), Theme.Text);
            TextRenderer.DrawText(g, ColorTranslator.ToHtml(Color.FromArgb(color.R, color.G, color.B)).ToUpperInvariant(), Theme.Small, new Point((int)(46 * s), (int)(22 * s)), Theme.Muted);
            if (Focused) using (var pen = new Pen(Theme.A2) { DashStyle = DashStyle.Dot }) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
        void Pick()
        {
            using (var d = new ColorDialog { Color = color, FullOpen = true, AnyColor = true })
                if (d.ShowDialog(FindForm()) == DialogResult.OK) { Value = d.Color; if (Changed != null) Changed(this, EventArgs.Empty); }
        }
        protected override void OnClick(EventArgs e) { Focus(); Pick(); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) Pick(); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); }
    }

    public class FlatButton : DControl
    {
        bool hover; public bool Primary;
        public FlatButton(string text) { Text = text; Height = 34; Cursor = Cursors.Hand; TabStop = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Smooth(g); g.Clear(BackColor);
            float s = S;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var p = Theme.Round(r, 8 * s))
            {
                if (Primary) using (var b = Theme.Accent(r)) g.FillPath(b, p);
                else using (var b = new SolidBrush(hover ? Theme.Track : Theme.Bg)) g.FillPath(b, p);
                using (var pen = new Pen(Focused ? Theme.A2 : Theme.Border)) g.DrawPath(pen, p);
            }
            TextRenderer.DrawText(g, Text, Theme.Bold, Rectangle.Round(r), Primary ? Color.White : Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) OnClick(EventArgs.Empty); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); }
    }

    public class SchemeTile : DControl
    {
        public Scheme Scheme; public bool Active; bool hover;
        public SchemeTile(Scheme s) { Scheme = s; Text = s.Name; Cursor = Cursors.Hand; TabStop = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Smooth(g); g.Clear(BackColor);
            float s = S;
            var r = new RectangleF(1, 1, Width - 3, Height - 3);
            using (var p = Theme.Round(r, 10 * s))
            {
                var bar = new RectangleF(r.X, r.Y, r.Width, r.Height - 22 * s);
                using (var bmp = new Bitmap(Math.Max(1, (int)bar.Width), 1))
                {
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        double fx = x / (double)bmp.Width;
                        var sc = Scheme;
                        Color c = sc.Effect == Effect.RainbowWave || sc.Effect == Effect.Spectrum ? Fx.Hsv(fx * 300, 1, 1)
                            : sc.Effect == Effect.GradientWave ? Fx.Lerp(sc.C1, sc.C2, fx)
                            : sc.Effect == Effect.Breathing ? Fx.Scale(sc.C1, 1 - Math.Abs(fx - 0.5) * 1.6)
                            : sc.Effect == Effect.Starlight ? (((int)(fx * 37)) % 7 == 0 ? sc.C1 : sc.C2)
                            : sc.Effect == Effect.Off ? Color.FromArgb(28, 30, 40) : sc.C1;
                        bmp.SetPixel(x, 0, c);
                    }
                    var st = g.Save();
                    g.SetClip(p);
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.DrawImage(bmp, bar);
                    using (var b = new SolidBrush(Theme.Bg)) g.FillRectangle(b, r.X, bar.Bottom, r.Width, r.Height - bar.Height);
                    g.Restore(st);
                }
                using (var pen = new Pen(Active ? Theme.A2 : hover ? Theme.Muted : Theme.Border, Active ? 2.5f * s : 1)) g.DrawPath(pen, p);
            }
            TextRenderer.DrawText(g, Text, Active ? Theme.Bold : Theme.Small, new Rectangle(0, (int)(Height - 22 * s), Width, (int)(20 * s)),
                Active ? Color.White : Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) OnClick(EventArgs.Empty); }
    }

    public class NavButton : DControl
    {
        public bool Active; bool hover; public string Glyph;
        public NavButton(string glyph, string text) { Glyph = glyph; Text = text; Height = 44; Cursor = Cursors.Hand; BackColor = Theme.Side; TabStop = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Smooth(g); g.Clear(BackColor);
            float s = S;
            var r = new RectangleF(8 * s, 2 * s, Width - 16 * s, Height - 4 * s);
            if (Active || hover)
                using (var p = Theme.Round(r, 8 * s)) using (var b = new SolidBrush(Active ? Theme.Card : Color.FromArgb(22, 24, 32))) g.FillPath(b, p);
            if (Active)
                using (var p = Theme.Round(new RectangleF(8 * s, 12 * s, 3 * s, Height - 24 * s), 1.5f * s)) using (var b = Theme.Accent(new RectangleF(0, 0, 4 * s, 4 * s))) g.FillPath(b, p);
            TextRenderer.DrawText(g, Glyph, Theme.F(12f), new Rectangle((int)(18 * s), 0, (int)(28 * s), Height), Active ? Theme.A2 : Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            TextRenderer.DrawText(g, Text, Active ? Theme.Bold : Font, new Rectangle((int)(50 * s), 0, Width, Height), Active ? Theme.Text : Theme.Muted, TextFormatFlags.VerticalCenter);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) OnClick(EventArgs.Empty); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); }
    }
}
