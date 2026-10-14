using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace DualPulse
{
    // Generic driver for the HID LampArray standard (usage page 0x59), the protocol behind
    // Windows Dynamic Lighting. Works with any compliant keyboard, mouse, headset or accessory.
    public class LampArrayDriver : IDriver
    {
        public IEnumerable<Device> Probe(List<HidInfo> all, HashSet<string> claimed)
        {
            foreach (var info in all.Where(i => i.UsagePage == 0x59 && i.Usage == 0x01 && i.FeatLen > 0))
            {
                if (claimed.Contains(info.Path)) continue;
                // a native driver already paints this device: skip the duplicate LampArray view
                if (claimed.Contains(string.Format("lit:{0:X4}:{1:X4}", info.Vid, info.Pid))) continue;
                LampArrayDevice d = null;
                try { d = new LampArrayDevice(info); }
                catch { if (d != null) d.Dispose(); d = null; }
                if (d != null) { claimed.Add(info.Path); d.Paths = new[] { info.Path }; yield return d; }
            }
        }
    }

    public class LampArrayDevice : Device
    {
        const ushort Page = 0x59;
        const ushort U_Attr = 0x02, U_LampCount = 0x03, U_Width = 0x04, U_Height = 0x05, U_Kind = 0x07, U_MinInterval = 0x08;
        const ushort U_Req = 0x20, U_LampId = 0x21, U_Resp = 0x22, U_PosX = 0x23, U_PosY = 0x24, U_RedLevels = 0x28;
        const ushort U_Multi = 0x50, U_Red = 0x51, U_Green = 0x52, U_Blue = 0x53, U_Intensity = 0x54, U_Flags = 0x55;
        const ushort U_Range = 0x60, U_Start = 0x61, U_End = 0x62, U_Control = 0x70, U_Autonomous = 0x71;

        readonly HidDevice dev;
        readonly List<Native.HIDP_VALUE_CAPS> caps;
        readonly int featLen;
        int perReport = 1, levels = 256;
        long minIntervalTicks;
        DateTime lastWrite = DateTime.MinValue, lastFull = DateTime.MinValue;
        Color[] sent;

        public LampArrayDevice(HidInfo info)
        {
            dev = new HidDevice(info);
            featLen = info.FeatLen;
            var hc = new Native.HIDP_CAPS();
            Native.HidP_GetCaps(dev.Preparsed, ref hc);
            ushort n = hc.NumberFeatureValueCaps;
            var arr = new Native.HIDP_VALUE_CAPS[n];
            Native.HidP_GetValueCaps(Native.HidP_Feature, arr, ref n, dev.Preparsed);
            caps = arr.Take(n).Where(c => c.UsagePage == Page && c.IsRange == 0).ToList();

            var attr = GetReport(U_Attr);
            if (attr == null) throw new InvalidOperationException("no LampArray attributes");
            LampCount = (int)Get(attr, U_Attr, U_LampCount);
            if (LampCount <= 0 || LampCount > 4096) throw new InvalidOperationException("bad lamp count");
            float w = Get(attr, U_Attr, U_Width), h = Get(attr, U_Attr, U_Height);
            uint kind = Get(attr, U_Attr, U_Kind);
            minIntervalTicks = Math.Min(Get(attr, U_Attr, U_MinInterval), 200000) * 10L;
            Kind = kind == 1 ? DeviceKind.Keyboard : kind == 2 ? DeviceKind.Mouse : kind == 8 ? DeviceKind.Headset : DeviceKind.Other;

            var pos = new PointF[LampCount];
            for (int i = 0; i < LampCount; i++)
            {
                var req = NewReport(U_Req);
                Set(req, U_Req, U_LampId, (uint)i);
                var resp = req == null || !dev.SetFeature(req) ? null : GetReport(U_Resp);
                if (resp != null)
                {
                    pos[i] = new PointF(Get(resp, U_Resp, U_PosX), Get(resp, U_Resp, U_PosY));
                    if (i == 0) { uint lv = Get(resp, U_Resp, U_RedLevels); if (lv > 1 && lv < 256) levels = (int)lv; }
                }
            }
            if (w <= 0) w = Math.Max(1, pos.Max(p => p.X));
            if (h <= 0) h = Math.Max(1, pos.Max(p => p.Y));
            LampPos = pos.Select(p => new PointF(Clamp(p.X / w), Clamp(p.Y / h))).ToArray();
            Aspect = Math.Max(0.35f, Math.Min(6f, w / h));
            Frame = new Color[LampCount];
            sent = new Color[LampCount];

            int ids = Slots(U_Multi, U_LampId), reds = Slots(U_Multi, U_Red);
            perReport = Math.Max(1, Math.Min(ids, reds));

            Vid = info.Vid; Pid = info.Pid;
            Id = string.Format("lamparray:{0:X4}:{1:X4}:{2}", Vid, Pid, LampCount);
            Driver = "Windows Dynamic Lighting (HID LampArray)";
            Name = Names.Friendly(info.Product, Vid, Kind);
        }

        static float Clamp(float v) { return float.IsNaN(v) ? 0.5f : Math.Max(0, Math.Min(1, v)); }

        Native.HIDP_VALUE_CAPS[] Caps(ushort coll, ushort usage)
        {
            return caps.Where(c => c.LinkUsage == coll && c.Usage == usage).OrderBy(c => c.DataIndex).ToArray();
        }

        int Slots(ushort coll, ushort usage) { return Caps(coll, usage).Sum(c => (int)c.ReportCount); }

        byte[] NewReport(ushort coll)
        {
            var c = caps.FirstOrDefault(x => x.LinkUsage == coll);
            if (c.UsagePage == 0) return null;
            var buf = new byte[featLen];
            Native.HidP_InitializeReportForID(Native.HidP_Feature, c.ReportID, dev.Preparsed, buf, (uint)buf.Length);
            return buf;
        }

        byte[] GetReport(ushort coll)
        {
            var buf = NewReport(coll);
            return buf != null && dev.GetFeature(buf) ? buf : null;
        }

        uint Get(byte[] report, ushort coll, ushort usage)
        {
            var c = Caps(coll, usage);
            if (c.Length == 0) return 0;
            uint v;
            return Native.HidP_GetUsageValue(Native.HidP_Feature, Page, c[0].LinkCollection, usage, out v, dev.Preparsed, report, (uint)report.Length) == Native.HIDP_STATUS_SUCCESS ? v : 0;
        }

        void Set(byte[] report, ushort coll, ushort usage, uint value)
        {
            var c = Caps(coll, usage);
            if (c.Length == 0 || report == null) return;
            Native.HidP_SetUsageValue(Native.HidP_Feature, Page, c[0].LinkCollection, usage, value, dev.Preparsed, report, (uint)report.Length);
        }

        // Fill slot k of a usage that is either one array cap or several repeated single caps.
        void SetSlots(byte[] report, ushort coll, ushort usage, uint[] values)
        {
            var c = Caps(coll, usage);
            if (c.Length == 1 && c[0].ReportCount > 1)
            {
                int bytes = (c[0].BitSize + 7) / 8;
                var raw = new byte[bytes * c[0].ReportCount];
                for (int i = 0; i < values.Length && i < c[0].ReportCount; i++)
                    for (int b = 0; b < bytes; b++) raw[i * bytes + b] = (byte)(values[i] >> (8 * b));
                Native.HidP_SetUsageValueArray(Native.HidP_Feature, Page, c[0].LinkCollection, usage, raw, (ushort)raw.Length, dev.Preparsed, report, (uint)report.Length);
            }
            else
            {
                var data = new List<Native.HIDP_DATA>();
                for (int i = 0; i < values.Length && i < c.Length; i++)
                    data.Add(new Native.HIDP_DATA { DataIndex = c[i].DataIndex, RawValue = values[i] });
                uint len = (uint)data.Count;
                if (len > 0) Native.HidP_SetData(Native.HidP_Feature, data.ToArray(), ref len, dev.Preparsed, report, (uint)report.Length);
            }
        }

        uint Lvl(byte v) { return levels == 256 ? v : (uint)Math.Round(v * (levels - 1) / 255.0); }

        public override void TakeControl()
        {
            var r = NewReport(U_Control);
            if (r != null) { Set(r, U_Control, U_Autonomous, 0); dev.SetFeature(r); }
            lastFull = DateTime.MinValue;
        }

        public override void ReleaseControl()
        {
            var r = NewReport(U_Control);
            if (r != null) { Set(r, U_Control, U_Autonomous, 1); dev.SetFeature(r); }
        }

        public override bool WriteLamps(Color[] colors)
        {
            var now = DateTime.Now;
            if ((now - lastWrite).Ticks < minIntervalTicks) return true;
            bool full = (now - lastFull).TotalSeconds > 2;
            var changed = new List<int>();
            for (int i = 0; i < LampCount; i++)
                if (full || colors[i].ToArgb() != sent[i].ToArgb()) changed.Add(i);
            if (changed.Count == 0) return true;

            bool ok = true;
            bool uniform = colors.All(c => c.ToArgb() == colors[0].ToArgb());
            if (uniform && Caps(U_Range, U_Start).Length > 0)
            {
                var r = NewReport(U_Range);
                Set(r, U_Range, U_Flags, 1);
                Set(r, U_Range, U_Start, 0); Set(r, U_Range, U_End, (uint)(LampCount - 1));
                Set(r, U_Range, U_Red, Lvl(colors[0].R)); Set(r, U_Range, U_Green, Lvl(colors[0].G));
                Set(r, U_Range, U_Blue, Lvl(colors[0].B)); Set(r, U_Range, U_Intensity, 255);
                ok = dev.SetFeature(r);
            }
            else
            {
                for (int s = 0; s < changed.Count && ok; s += perReport)
                {
                    var batch = changed.Skip(s).Take(perReport).ToArray();
                    var r = NewReport(U_Multi);
                    Set(r, U_Multi, U_LampCount, (uint)batch.Length);
                    Set(r, U_Multi, U_Flags, s + perReport >= changed.Count ? 1u : 0u);
                    SetSlots(r, U_Multi, U_LampId, batch.Select(i => (uint)i).ToArray());
                    SetSlots(r, U_Multi, U_Red, batch.Select(i => Lvl(colors[i].R)).ToArray());
                    SetSlots(r, U_Multi, U_Green, batch.Select(i => Lvl(colors[i].G)).ToArray());
                    SetSlots(r, U_Multi, U_Blue, batch.Select(i => Lvl(colors[i].B)).ToArray());
                    SetSlots(r, U_Multi, U_Intensity, batch.Select(i => 255u).ToArray());
                    ok = dev.SetFeature(r);
                }
            }
            if (!ok) { Alive = false; return false; }
            foreach (var i in changed) sent[i] = colors[i];
            lastWrite = now;
            if (full) lastFull = now;
            return true;
        }

        public override void Dispose() { dev.Dispose(); }
    }

    public static class Names
    {
        static readonly Dictionary<ushort, string> Vendors = new Dictionary<ushort, string> {
            { 0x046D, "Logitech" }, { 0x1B1C, "Corsair" }, { 0x1532, "Razer" }, { 0x0B05, "ASUS" }, { 0x1038, "SteelSeries" },
            { 0x0951, "HyperX" }, { 0x03F0, "HyperX" }, { 0x3434, "Keychron" }, { 0x045E, "Microsoft" }, { 0x2516, "Cooler Master" },
            { 0x1E7D, "ROCCAT" }, { 0x3367, "Endgame Gear" }, { 0x258A, "Glorious" }, { 0x320F, "Glorious" }, { 0x0738, "Mad Catz" },
            { 0x24AE, "Wooting" }, { 0x31E3, "Wooting" }, { 0x04D9, "Holtek" }, { 0x0C45, "Redragon" }, { 0x3554, "VXE" } };

        public static string Vendor(ushort vid) { string v; return Vendors.TryGetValue(vid, out v) ? v : string.Format("Device {0:X4}", vid); }

        public static string Friendly(string product, ushort vid, DeviceKind kind)
        {
            string p = (product ?? "").Trim();
            if (p.Length == 0 || p.IndexOf("VHF", StringComparison.OrdinalIgnoreCase) >= 0 || p.StartsWith("HID", StringComparison.OrdinalIgnoreCase))
                return Vendor(vid) + " " + kind.ToString().ToLowerInvariant();
            string[] noise = { "Mechanical", "Gaming", "Keyboard", "Mouse", "Optical", "Wireless", "Headset", "RGB Mouse" };
            bool again = true;
            while (again)
            {
                again = false;
                foreach (var w in noise)
                    if (p.EndsWith(" " + w, StringComparison.OrdinalIgnoreCase)) { p = p.Substring(0, p.Length - w.Length - 1).TrimEnd(); again = true; }
            }
            if (p.Length == 0) return Vendor(vid) + " " + kind.ToString().ToLowerInvariant();
            if (p.IndexOf(Vendor(vid), StringComparison.OrdinalIgnoreCase) < 0 && Vendors.ContainsKey(vid)) p = Vendor(vid) + " " + p;
            return p;
        }
    }
}
