using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace DualPulse
{
    // Corsair peripheral protocol v2 ("Bragi"-era wired devices) on the vendor collection (page FF42, 64+1 bytes).
    //   GET  08 02 <prop> 00              -> 00 02 <status> <value LE...>
    //   SET  08 01 <prop> 00 <value LE>    props: 0x01 polling rate, 0x02 brightness (0-1000), 0x03 mode (1 hw / 2 sw)
    //   OPEN 08 0D 00 <endpoint>           endpoint 0x01 = lighting (software mode only)
    //   WRITE 08 06 00 <len u32> <data>, then 08 07 00 <data> continuations
    // Hardware settings work on any v2 device. Per-key lighting needs the LED count and map,
    // so it is enabled for the models listed in Models below.
    public class CorsairDriver : IDriver
    {
        public class Model
        {
            public string Name; public DeviceKind Kind; public int Leds; public KbLayout Layout;
        }

        public static readonly Dictionary<ushort, Model> Models = new Dictionary<ushort, Model> {
            { 0x1BFD, new Model { Name = "Corsair K70 CORE RGB", Kind = DeviceKind.Keyboard, Leds = 123, Layout = KbLayout.FullSizeAnsi } },
        };

        public IEnumerable<Device> Probe(List<HidInfo> all, HashSet<string> claimed)
        {
            var found = new List<Device>();
            foreach (var info in all.Where(i => i.Vid == 0x1B1C && i.UsagePage == 0xFF42 && i.OutLen == 65))
            {
                if (claimed.Contains(info.Path)) continue;
                CorsairDevice d = null;
                try { d = new CorsairDevice(info); }
                catch (TimeoutException)
                {
                    // the vendor interface exists but the firmware is not answering (not claimed, so it is retried)
                    Model m;
                    var kind = Models.TryGetValue(info.Pid, out m) ? m.Kind
                        : (info.Product ?? "").IndexOf("mouse", StringComparison.OrdinalIgnoreCase) >= 0 ? DeviceKind.Mouse : DeviceKind.Keyboard;
                    found.Add(new UnresponsiveDevice(info, kind, m != null ? m.Name : Names.Friendly(info.Product, info.Vid, kind),
                        "Corsair protocol v2", "Not responding • unplug and replug the USB cable"));
                    continue;
                }
                catch { d = null; }
                if (d == null) continue;
                claimed.Add(info.Path);
                d.Paths = new[] { info.Path };
                if (d.HasLighting) claimed.Add(string.Format("lit:{0:X4}:{1:X4}", d.Vid, d.Pid));
                found.Add(d);
            }
            return found;
        }
    }

    public class CorsairDevice : Device, IHwSettings
    {
        readonly HidDevice dev;
        readonly object io = new object();
        bool endpointOpen, software;
        public bool Interleaved;   // alternative colour layout (RGBRGB...) instead of planar
        public string LastError = "";

        public CorsairDevice(HidInfo info)
        {
            dev = new HidDevice(info);
            var r = Cmd(0x02, 0x12, 0x00);   // read back the PID property to confirm the protocol
            if (r == null) { dev.Dispose(); throw new TimeoutException("no response"); }
            if (r[3] != 0) { dev.Dispose(); throw new InvalidOperationException("not a Corsair v2 device"); }
            Vid = info.Vid; Pid = info.Pid;
            CorsairDriver.Model m;
            if (CorsairDriver.Models.TryGetValue(Pid, out m))
            {
                Name = m.Name; Kind = m.Kind; Layout = m.Layout;
                LampCount = m.Leds; Frame = new Color[m.Leds];
                LampPos = new PointF[m.Leds];
                for (int i = 0; i < m.Leds; i++) LampPos[i] = m.Layout != null ? m.Layout.LedPos(i) : new PointF((i + 0.5f) / m.Leds, 0.5f);
                Aspect = m.Layout != null ? m.Layout.Width / m.Layout.Height : 3f;
            }
            else
            {
                string p = info.Product ?? "";
                Kind = p.IndexOf("keyboard", StringComparison.OrdinalIgnoreCase) >= 0 ? DeviceKind.Keyboard
                    : p.IndexOf("mouse", StringComparison.OrdinalIgnoreCase) >= 0 ? DeviceKind.Mouse
                    : p.IndexOf("headset", StringComparison.OrdinalIgnoreCase) >= 0 ? DeviceKind.Headset : DeviceKind.Other;
                Name = Names.Friendly(p, Vid, Kind);
            }
            Driver = "Corsair protocol v2" + (LampCount == 0 ? " (lighting map not known yet)" : "");
            Id = string.Format("corsair:{0:X4}", Pid);
        }

        byte[] Cmd(params byte[] p)
        {
            lock (io)
            {
                var m = new byte[65]; m[1] = 0x08;
                Array.Copy(p, 0, m, 2, Math.Min(p.Length, 63));
                try
                {
                    dev.Write(m);
                    for (int i = 0; i < 8; i++)
                    {
                        var r = dev.Read(250);
                        if (r == null) return null;
                        if (r[2] == p[0]) return r;
                    }
                }
                catch (Exception e) { LastError = e.Message; Alive = false; }
                return null;
            }
        }

        bool Ok(byte[] r) { if (r != null && r[3] != 0) LastError = "Corsair status " + r[3]; return r != null && r[3] == 0; }
        int GetProp(byte prop) { var r = Cmd(0x02, prop, 0x00); return r == null || r[3] != 0 ? -1 : r[4] | (r[5] << 8); }
        bool SetProp(byte prop, int v) { return Ok(Cmd(0x01, prop, 0x00, (byte)v, (byte)(v >> 8))); }

        bool SetSoftware(bool sw)
        {
            endpointOpen = false;
            if (!SetProp(0x03, sw ? 2 : 1)) return false;
            software = sw;
            if (sw) endpointOpen = Ok(Cmd(0x0D, 0x00, 0x01));
            return !sw || endpointOpen;
        }

        public override IHwSettings Hw { get { return this; } }
        public override void TakeControl() { if (HasLighting) SetSoftware(true); }
        public override void ReleaseControl() { if (software) SetSoftware(false); }

        public override bool WriteLamps(Color[] leds)
        {
            if (!HasLighting) return true;
            if (!endpointOpen && !SetSoftware(true)) return false;
            int n = LampCount;
            var data = new byte[2 + n * 3];
            data[0] = 0x12; data[1] = 0x00;   // colour data type
            for (int i = 0; i < n; i++)
            {
                Color c = leds[i];
                if (Interleaved) { data[2 + i * 3] = c.R; data[3 + i * 3] = c.G; data[4 + i * 3] = c.B; }
                else { data[2 + i] = c.R; data[2 + n + i] = c.G; data[2 + 2 * n + i] = c.B; }
            }
            lock (io)
            {
                int pos = 0; bool first = true;
                while (pos < data.Length)
                {
                    var p = new List<byte>();
                    if (first) p.AddRange(new byte[] { 0x06, 0x00, (byte)data.Length, (byte)(data.Length >> 8), 0, 0 });
                    else p.AddRange(new byte[] { 0x07, 0x00 });
                    int k = Math.Min(63 - p.Count, data.Length - pos);
                    for (int i = 0; i < k; i++) p.Add(data[pos + i]);
                    pos += k; first = false;
                    if (!Ok(Cmd(p.ToArray()))) { endpointOpen = false; return Alive; }
                }
            }
            return true;
        }

        // IHwSettings
        public bool HasBrightness { get { return true; } }
        public int GetBrightness() { int v = GetProp(0x02); return v < 0 ? -1 : (int)Math.Round(v / 10.0); }
        public bool SetBrightness(int pct) { return SetProp(0x02, Math.Max(0, Math.Min(100, pct)) * 10); }
        public int[] PollRates { get { return new[] { 125, 250, 500, 1000 }; } }
        public int GetPoll() { int v = GetProp(0x01); return v >= 1 && v <= 7 ? 125 << (v - 1) : 0; }
        public bool SetPoll(int hz) { int v = 1; while ((125 << v) <= hz && v < 7) v++; return SetProp(0x01, v); }

        public override void Dispose() { dev.Dispose(); }
    }

    // Physical key layout in key units, plus the LED index of each key.
    public class KbLayout
    {
        public class Key { public string Label; public int Usage; public float X, Y, W, H; public int Led; }
        public readonly List<Key> Keys = new List<Key>();
        public float Width = 22.5f, Height = 6.5f;
        readonly Dictionary<int, PointF> ledPos = new Dictionary<int, PointF>();
        readonly Func<int, int> ledOf;

        public PointF LedPos(int led) { PointF p; return ledPos.TryGetValue(led, out p) ? p : new PointF(0.5f, 0.5f); }

        KbLayout(Func<int, int> ledOf) { this.ledOf = ledOf; }

        void Row(float y, float x, params object[] items)
        {
            int i = 0;
            while (i < items.Length)
            {
                if (items[i] is float) { x += (float)items[i]; i++; continue; }
                string l = (string)items[i]; int u = (int)items[i + 1]; float w = (float)items[i + 2];
                float h = 1;
                if (l.EndsWith("|")) { l = l.TrimEnd('|'); h = 2; }
                Keys.Add(new Key { Label = l, Usage = u, X = x, Y = y, W = w, H = h, Led = ledOf(u) });
                x += w; i += 3;
            }
        }

        void Finish()
        {
            foreach (var k in Keys)
                if (k.Led >= 0) ledPos[k.Led] = new PointF((k.X + k.W / 2) / Width, (k.Y + k.H / 2) / Height);
        }

        // Full-size ANSI board. Corsair v2 LED order: HID usage - 4 for regular keys, modifiers after the 0x65 block.
        public static readonly KbLayout FullSizeAnsi = BuildFullSize(u => u >= 0x04 && u <= 0x65 ? u - 4 : u >= 0xE0 && u <= 0xE7 ? 101 + u - 0xE0 : -1);

        static KbLayout BuildFullSize(Func<int, int> ledOf)
        {
            var L = new KbLayout(ledOf);
            float n = 15.25f, p = 18.5f;
            L.Row(0, 0, "Esc", 0x29, 1f, 1f, "F1", 0x3A, 1f, "F2", 0x3B, 1f, "F3", 0x3C, 1f, "F4", 0x3D, 1f, 0.5f,
                "F5", 0x3E, 1f, "F6", 0x3F, 1f, "F7", 0x40, 1f, "F8", 0x41, 1f, 0.5f,
                "F9", 0x42, 1f, "F10", 0x43, 1f, "F11", 0x44, 1f, "F12", 0x45, 1f, 0.25f,
                "Prt", 0x46, 1f, "Scr", 0x47, 1f, "Pau", 0x48, 1f);
            L.Row(1.5f, 0, "`", 0x35, 1f, "1", 0x1E, 1f, "2", 0x1F, 1f, "3", 0x20, 1f, "4", 0x21, 1f, "5", 0x22, 1f, "6", 0x23, 1f,
                "7", 0x24, 1f, "8", 0x25, 1f, "9", 0x26, 1f, "0", 0x27, 1f, "-", 0x2D, 1f, "=", 0x2E, 1f, "Back", 0x2A, 2f);
            L.Row(2.5f, 0, "Tab", 0x2B, 1.5f, "Q", 0x14, 1f, "W", 0x1A, 1f, "E", 0x08, 1f, "R", 0x15, 1f, "T", 0x17, 1f, "Y", 0x1C, 1f,
                "U", 0x18, 1f, "I", 0x0C, 1f, "O", 0x12, 1f, "P", 0x13, 1f, "[", 0x2F, 1f, "]", 0x30, 1f, "\\", 0x31, 1.5f);
            L.Row(3.5f, 0, "Caps", 0x39, 1.75f, "A", 0x04, 1f, "S", 0x16, 1f, "D", 0x07, 1f, "F", 0x09, 1f, "G", 0x0A, 1f, "H", 0x0B, 1f,
                "J", 0x0D, 1f, "K", 0x0E, 1f, "L", 0x0F, 1f, ";", 0x33, 1f, "'", 0x34, 1f, "Enter", 0x28, 2.25f);
            L.Row(4.5f, 0, "Shift", 0xE1, 2.25f, "Z", 0x1D, 1f, "X", 0x1B, 1f, "C", 0x06, 1f, "V", 0x19, 1f, "B", 0x05, 1f, "N", 0x11, 1f,
                "M", 0x10, 1f, ",", 0x36, 1f, ".", 0x37, 1f, "/", 0x38, 1f, "Shift", 0xE5, 2.75f);
            L.Row(5.5f, 0, "Ctrl", 0xE0, 1.25f, "Win", 0xE3, 1.25f, "Alt", 0xE2, 1.25f, "", 0x2C, 6.25f, "Alt", 0xE6, 1.25f,
                "Fn", 0, 1.25f, "Menu", 0x65, 1.25f, "Ctrl", 0xE4, 1.25f);
            L.Row(1.5f, n, "Ins", 0x49, 1f, "Hm", 0x4A, 1f, "PU", 0x4B, 1f);
            L.Row(2.5f, n, "Del", 0x4C, 1f, "End", 0x4D, 1f, "PD", 0x4E, 1f);
            L.Row(4.5f, n + 1, "▲", 0x52, 1f);
            L.Row(5.5f, n, "◀", 0x50, 1f, "▼", 0x51, 1f, "▶", 0x4F, 1f);
            L.Row(1.5f, p, "Num", 0x53, 1f, "/", 0x54, 1f, "*", 0x55, 1f, "-", 0x56, 1f);
            L.Row(2.5f, p, "7", 0x5F, 1f, "8", 0x60, 1f, "9", 0x61, 1f, "+|", 0x57, 1f);
            L.Row(3.5f, p, "4", 0x5C, 1f, "5", 0x5D, 1f, "6", 0x5E, 1f);
            L.Row(4.5f, p, "1", 0x59, 1f, "2", 0x5A, 1f, "3", 0x5B, 1f, "⏎|", 0x58, 1f);
            L.Row(5.5f, p, "0", 0x62, 2f, ".", 0x63, 1f);
            L.Finish();
            return L;
        }
    }
}
