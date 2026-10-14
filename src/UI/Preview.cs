using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace DualPulse
{
    // Live preview of what is being sent to every lit device, laid out like the shared effect space.
    public class DevicePreview : DControl
    {
        public Engine Engine;
        public DevicePreview() { BackColor = Theme.Card; }

        static readonly Color Off = Color.FromArgb(40, 42, 52), Body = Color.FromArgb(30, 32, 40);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Smooth(g); g.Clear(BackColor);
            if (Engine == null) return;
            float s = S;
            var layout = Engine.Layout();
            if (layout.Count == 0)
            {
                TextRenderer.DrawText(g, "No lighting devices found yet.\nPlug one in, or check the Devices page.", Theme.Body, ClientRectangle, Theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            float labelH = 22 * s;
            float total = layout.Last().X0 + layout.Last().W;
            float u = Math.Min((Width - 8) / total, Height - labelH - 4);
            float ox = (Width - total * u) / 2, oy = (Height - labelH - u) / 2;
            double k = 0.25 + 0.75 * Engine.S.Brightness / 100.0;

            foreach (var pl in layout)
            {
                var d = pl.Dev;
                var r = new RectangleF(ox + pl.X0 * u, oy, pl.W * u, u);
                var frame = d.Frame;
                Func<int, Color> col = i => i < frame.Length ? Fx.Scale(frame[i], k) : Off;

                if (d.Layout != null) DrawKeyboard(g, d, r, col, s);
                else if (d.Kind == DeviceKind.Mouse) DrawMouse(g, d, r, col, s);
                else DrawGeneric(g, d, r, col, s);

                var lr = new Rectangle((int)(r.X - 30 * s), (int)(r.Bottom + 4 * s), (int)(r.Width + 60 * s), (int)(18 * s));
                TextRenderer.DrawText(g, d.Name, Theme.Small, lr, Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
            }
        }

        static void DrawKeyboard(Graphics g, Device d, RectangleF r, Func<int, Color> col, float s)
        {
            var L = d.Layout;
            float ku = Math.Min(r.Width / L.Width, r.Height / L.Height);
            float ox = r.X + (r.Width - ku * L.Width) / 2, oy = r.Y + (r.Height - ku * L.Height) / 2;
            using (var body = Theme.Round(new RectangleF(ox - 6 * s, oy - 6 * s, ku * L.Width + 12 * s, ku * L.Height + 12 * s), 10 * s))
            using (var b = new SolidBrush(Color.FromArgb(14, 15, 20))) g.FillPath(b, body);
            foreach (var key in L.Keys)
            {
                var kr = new RectangleF(ox + key.X * ku + 1.5f, oy + key.Y * ku + 1.5f, key.W * ku - 3, key.H * ku - 3);
                Color c = key.Led >= 0 && key.Led < d.LampCount ? col(key.Led) : Fx.Lerp(col(0), Off, 0.5);
                using (var p = Theme.Round(kr, 3 * s))
                {
                    using (var b = new SolidBrush(Body)) g.FillPath(b, p);
                    using (var pen = new Pen(c, Math.Max(1.5f, 1.6f * s))) g.DrawPath(pen, p);
                }
                if (ku > 24 * s && key.Label.Length > 0)
                    TextRenderer.DrawText(g, key.Label, Theme.F(6.5f), Rectangle.Round(kr), Fx.Lerp(c, Color.White, 0.35),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        static void DrawMouse(Graphics g, Device d, RectangleF r, Func<int, Color> col, float s)
        {
            float w = Math.Min(r.Width, r.Height * 0.58f), h = Math.Min(r.Height, w * 1.75f);
            var mr = new RectangleF(r.X + (r.Width - w) / 2, r.Y + (r.Height - h) / 2, w, h);
            using (var p = Theme.Round(mr, w * 0.48f))
            {
                using (var b = new SolidBrush(Body)) g.FillPath(b, p);
                using (var pen = new Pen(Theme.Border, 1.5f * s)) g.DrawPath(pen, p);
            }
            using (var pen = new Pen(Theme.Border, 1.5f * s)) g.DrawLine(pen, mr.X + w / 2, mr.Y + 4 * s, mr.X + w / 2, mr.Y + h * 0.32f);
            float lr = Math.Max(3 * s, w * (d.LampCount <= 2 ? 0.1f : d.LampCount <= 8 ? 0.09f : 0.05f));
            for (int i = 0; i < d.LampCount; i++)
            {
                var pt = d.LampPos[i];
                float cx = mr.X + pt.X * w, cy = mr.Y + pt.Y * h;
                using (var b = new SolidBrush(col(i))) g.FillEllipse(b, cx - lr, cy - lr, lr * 2, lr * 2);
            }
        }

        static void DrawGeneric(Graphics g, Device d, RectangleF r, Func<int, Color> col, float s)
        {
            using (var p = Theme.Round(r, 10 * s))
            {
                using (var b = new SolidBrush(Color.FromArgb(14, 15, 20))) g.FillPath(b, p);
                using (var pen = new Pen(Theme.Border, 1.5f * s)) g.DrawPath(pen, p);
            }
            float lr = Math.Max(2.5f * s, Math.Min(r.Width, r.Height) / (float)(Math.Sqrt(Math.Max(1, d.LampCount)) * 3.2));
            var inner = RectangleF.Inflate(r, -lr - 4 * s, -lr - 4 * s);
            for (int i = 0; i < d.LampCount; i++)
            {
                var pt = d.LampPos[i];
                float cx = inner.X + pt.X * inner.Width, cy = inner.Y + pt.Y * inner.Height;
                using (var b = new SolidBrush(col(i))) g.FillEllipse(b, cx - lr, cy - lr, lr * 2, lr * 2);
            }
        }
    }

    // One row on the Devices page.
    public class DeviceRow : Panel
    {
        public readonly Device Dev;
        public readonly Toggle Sync;
        public DeviceRow(Device d, bool lit)
        {
            Dev = d; BackColor = Theme.Card; Height = 64;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            if (d.HasLighting)
            {
                Sync = new Toggle("Sync lighting") { Width = 170, BackColor = Theme.Card, Anchor = AnchorStyles.Right | AnchorStyles.Top };
                Sync.On = lit;
                Controls.Add(Sync);
            }
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Sync != null) Sync.Location = new Point(Width - Sync.Width - 8, (Height - Sync.Height) / 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Smooth(g); g.Clear(BackColor);
            float s = g.DpiX / 96f;
            var ic = new RectangleF(8 * s, (Height - 40 * s) / 2, 40 * s, 40 * s);
            using (var p = Theme.Round(ic, 10 * s)) using (var b = new SolidBrush(Theme.Bg)) g.FillPath(b, p);
            string glyph = Dev.Kind == DeviceKind.Keyboard ? "⌨" : Dev.Kind == DeviceKind.Mouse ? "◉" : Dev.Kind == DeviceKind.Headset ? "♫" : "✦";
            TextRenderer.DrawText(g, glyph, Theme.F(13f), Rectangle.Round(ic), Theme.A2, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, Dev.Name, Theme.Bold, new Point((int)(60 * s), (int)(12 * s)), Theme.Text, TextFormatFlags.NoPrefix);
            var caps = new System.Collections.Generic.List<string> { Dev.Driver };
            if (Dev.HasLighting) caps.Add(Dev.LampCount + (Dev.LampCount == 1 ? " lamp" : " lamps"));
            if (Dev.Sensor != null) caps.Add("DPI " + Dev.Sensor.DpiMin + "–" + Dev.Sensor.DpiMax);
            if (Dev.Hw != null) caps.Add("brightness + polling");
            TextRenderer.DrawText(g, string.Join("  •  ", caps), Theme.Small, new Point((int)(60 * s), (int)(34 * s)), Theme.Muted, TextFormatFlags.NoPrefix);
            using (var pen = new Pen(Theme.Border)) g.DrawLine(pen, 8 * s, Height - 1, Width - 8 * s, Height - 1);
        }
    }
}
