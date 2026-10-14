using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DualPulse
{
    static class Win
    {
        [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint a, uint u, IntPtr p, uint w);
        [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint a, uint u, ref int p, uint w);
        [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint a, uint u, int[] p, uint w);
        [DllImport("user32.dll")] public static extern uint GetDoubleClickTime();
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        const uint Save = 3; // SPIF_UPDATEINIFILE | SPIF_SENDCHANGE

        static int Get(uint a) { int v = 0; SystemParametersInfo(a, 0, ref v, 0); return v; }
        public static int PointerSpeed { get { return Get(0x70); } set { SystemParametersInfo(0x71, 0, (IntPtr)value, Save); } }
        public static bool Precision
        {
            get { var m = new int[3]; SystemParametersInfo(0x03, 0, m, 0); return m[2] != 0; }
            set { SystemParametersInfo(0x04, 0, value ? new[] { 6, 10, 1 } : new[] { 0, 0, 0 }, Save); }
        }
        public static int ScrollLines { get { return Get(0x68); } set { SystemParametersInfo(0x69, (uint)value, IntPtr.Zero, Save); } }
        public static int DoubleClick { get { return (int)GetDoubleClickTime(); } set { SystemParametersInfo(0x20, (uint)value, IntPtr.Zero, Save); } }
        public static int RepeatDelay { get { return Get(0x16); } set { SystemParametersInfo(0x17, (uint)value, IntPtr.Zero, Save); } }
        public static int RepeatRate { get { return Get(0x0A); } set { SystemParametersInfo(0x0B, (uint)value, IntPtr.Zero, Save); } }

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public static bool Autostart
        {
            get { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue("DualPulse") != null; }
            set
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                    if (value) k.SetValue("DualPulse", "\"" + Application.ExecutablePath + "\" --tray");
                    else k.DeleteValue("DualPulse", false);
            }
        }
    }

    static class Brand
    {
        public static Bitmap Logo(int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float s = size;
                var r = new RectangleF(s * 0.04f, s * 0.04f, s * 0.92f, s * 0.92f);
                using (var p = Theme.Round(r, s * 0.24f))
                using (var b = new LinearGradientBrush(r, Theme.A1, Theme.A2, 45f)) g.FillPath(b, p);
                // two interlocking rings = "dual"
                float w = Math.Max(1.5f, s * 0.085f), d = s * 0.42f;
                using (var pen = new Pen(Color.White, w))
                {
                    g.DrawEllipse(pen, s * 0.15f, s * 0.29f, d, d);
                    g.DrawEllipse(pen, s * 0.43f, s * 0.29f, d, d);
                }
                // pulse line
                using (var pen = new Pen(Color.FromArgb(18, 12, 40), Math.Max(1.2f, s * 0.05f)) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLines(pen, new[] { new PointF(s * 0.2f, s * 0.5f), new PointF(s * 0.38f, s * 0.5f), new PointF(s * 0.45f, s * 0.36f),
                        new PointF(s * 0.53f, s * 0.66f), new PointF(s * 0.6f, s * 0.5f), new PointF(s * 0.8f, s * 0.5f) });
            }
            return bmp;
        }

        public static Icon AppIcon(int size) { using (var b = Logo(size)) return Icon.FromHandle(b.GetHicon()); }

        public static void WriteIco(string path)
        {
            int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
            var pngs = sizes.Select(sz => { using (var b = Logo(sz)) using (var ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); return ms.ToArray(); } }).ToArray();
            using (var w = new BinaryWriter(File.Create(path)))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int off = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
                    w.Write(pngs[i].Length); w.Write(off); off += pngs[i].Length;
                }
                foreach (var p in pngs) w.Write(p);
            }
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--make-icon") { Brand.WriteIco(args[1]); return; }
            bool created;
            using (var mutex = new Mutex(true, "DualPulse.SingleInstance", out created))
            using (var signal = new EventWaitHandle(false, EventResetMode.AutoReset, "DualPulse.Show"))
            using (var quit = new EventWaitHandle(false, EventResetMode.AutoReset, "DualPulse.Exit"))
            {
                // a second launch shows the running window; "--exit" asks it to quit cleanly
                if (!created) { if (args.Contains("--exit")) quit.Set(); else signal.Set(); return; }
                if (args.Contains("--exit")) return;
                try { Win.SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                var s = Settings.Load();
                var engine = new Engine(s);
                engine.Start();
                var form = new MainForm(s, engine, args.Contains("--tray"));
                var waiter = new Thread(() =>
                {
                    while (true)
                    {
                        int which = WaitHandle.WaitAny(new WaitHandle[] { signal, quit });
                        try { form.BeginInvoke(new Action(which == 0 ? (Action)form.ShowFromTray : form.ExitApp)); } catch { return; }
                    }
                }) { IsBackground = true };
                waiter.Start();
                Application.Run(form);
            }
        }
    }
}
