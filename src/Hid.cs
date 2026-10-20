using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace DualPulse
{
    public class HidInfo
    {
        public string Path, Product;
        public ushort Vid, Pid;
        public ushort UsagePage, Usage;
        public int InLen, OutLen, FeatLen;
        public override string ToString()
        {
            return string.Format("{0:X4}:{1:X4} page={2:X4} usage={3:X4} in={4} out={5} feat={6} '{7}'",
                Vid, Pid, UsagePage, Usage, InLen, OutLen, FeatLen, Product);
        }
    }

    // One opened HID top-level collection. Interrupt reports use raw overlapped ReadFile/WriteFile
    // (every call has a timeout and is safely cancelled), feature reports use HidD_Get/SetFeature,
    // and the HID parser is available via Preparsed.
    public class HidDevice : IDisposable
    {
        public readonly HidInfo Info;
        public IntPtr Preparsed { get; private set; }
        SafeFileHandle handle;
        readonly object ioLock = new object();

        public HidDevice(HidInfo info)
        {
            Info = info;
            handle = Native.CreateFile(info.Path, Native.GENERIC_READ | Native.GENERIC_WRITE,
                Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING,
                Native.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (handle.IsInvalid) throw new IOException("Cannot open HID device (error " + Marshal.GetLastWin32Error() + ")");
            IntPtr pp;
            if (Native.HidD_GetPreparsedData(handle, out pp)) Preparsed = pp;
        }

        // data[0] is the report id; the buffer is padded to the output report length.
        public void Write(byte[] data, int timeoutMs = 500)
        {
            byte[] buf = new byte[Math.Max(Info.OutLen, data.Length)];
            Array.Copy(data, buf, data.Length);
            int n = Io(true, buf, timeoutMs);
            if (n == -1) throw new TimeoutException("HID write timed out");
            if (n < 0) throw new IOException("HID write failed (error " + (-n - 2) + ")");
        }

        // Returns null on timeout. Throws if the device is gone.
        public byte[] Read(int timeoutMs)
        {
            byte[] buf = new byte[Info.InLen];
            int n = Io(false, buf, timeoutMs);
            if (n == -1) return null;
            if (n < 0) throw new IOException("HID read failed (error " + (-n - 2) + ")");
            return n == 0 ? null : buf;
        }

        // Overlapped I/O with our own OVERLAPPED and event. On timeout the request is cancelled and
        // GetOverlappedResult(wait) is used to make sure it has fully completed before the buffers are freed.
        // Returns bytes transferred, -1 on timeout, or -(error + 2) on failure.
        int Io(bool write, byte[] buf, int timeoutMs)
        {
            lock (ioLock)
            {
                int ovSize = IntPtr.Size * 2 + 8 + IntPtr.Size;
                IntPtr ov = Marshal.AllocHGlobal(ovSize), mem = Marshal.AllocHGlobal(buf.Length);
                IntPtr ev = Native.CreateEvent(IntPtr.Zero, true, false, null);
                try
                {
                    for (int i = 0; i < ovSize; i++) Marshal.WriteByte(ov, i, 0);
                    Marshal.WriteIntPtr(ov, IntPtr.Size * 2 + 8, ev);
                    if (write) Marshal.Copy(buf, 0, mem, buf.Length);
                    bool ok = write ? Native.WriteFile(handle, mem, buf.Length, IntPtr.Zero, ov)
                                    : Native.ReadFile(handle, mem, buf.Length, IntPtr.Zero, ov);
                    int err = ok ? 0 : Marshal.GetLastWin32Error();
                    if (!ok && err != Native.ERROR_IO_PENDING) return -(err + 2);
                    int done;
                    if (Native.WaitForSingleObject(ev, (uint)timeoutMs) != 0)
                    {
                        Native.CancelIoEx(handle, ov);
                        Native.GetOverlappedResult(handle, ov, out done, true);
                        return -1;
                    }
                    if (!Native.GetOverlappedResult(handle, ov, out done, false)) return -(Marshal.GetLastWin32Error() + 2);
                    if (!write) Marshal.Copy(mem, buf, 0, Math.Min(done, buf.Length));
                    return done;
                }
                finally
                {
                    Native.CloseHandle(ev);
                    Marshal.FreeHGlobal(mem);
                    Marshal.FreeHGlobal(ov);
                }
            }
        }

        public bool GetFeature(byte[] buf) { return Native.HidD_GetFeature(handle, buf, buf.Length); }
        public bool SetFeature(byte[] buf) { return Native.HidD_SetFeature(handle, buf, buf.Length); }

        public void Dispose()
        {
            try { if (handle != null) handle.Dispose(); } catch { }
            if (Preparsed != IntPtr.Zero) { Native.HidD_FreePreparsedData(Preparsed); Preparsed = IntPtr.Zero; }
        }

        // vid = 0 enumerates every HID collection on the system.
        public static List<HidInfo> Enumerate(ushort vid = 0, ushort pid = 0)
        {
            var list = new List<HidInfo>();
            Guid g;
            Native.HidD_GetHidGuid(out g);
            IntPtr set = Native.SetupDiGetClassDevs(ref g, null, IntPtr.Zero, Native.DIGCF_PRESENT | Native.DIGCF_DEVICEINTERFACE);
            try
            {
                var did = new Native.SP_DEVICE_INTERFACE_DATA();
                did.cbSize = Marshal.SizeOf(did);
                for (int i = 0; Native.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref did); i++)
                {
                    int size;
                    Native.SetupDiGetDeviceInterfaceDetail(set, ref did, IntPtr.Zero, 0, out size, IntPtr.Zero);
                    IntPtr detail = Marshal.AllocHGlobal(size);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (!Native.SetupDiGetDeviceInterfaceDetail(set, ref did, detail, size, out size, IntPtr.Zero)) continue;
                        string path = Marshal.PtrToStringAuto(new IntPtr(detail.ToInt64() + 4));
                        var info = Query(path);
                        if (info != null && (vid == 0 || info.Vid == vid) && (pid == 0 || info.Pid == pid)) list.Add(info);
                    }
                    finally { Marshal.FreeHGlobal(detail); }
                }
            }
            finally { Native.SetupDiDestroyDeviceInfoList(set); }
            return list;
        }

        static HidInfo Query(string path)
        {
            using (var h = Native.CreateFile(path, 0, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero))
            {
                if (h.IsInvalid) return null;
                var attr = new Native.HIDD_ATTRIBUTES();
                attr.Size = Marshal.SizeOf(attr);
                if (!Native.HidD_GetAttributes(h, ref attr)) return null;
                var info = new HidInfo { Path = path, Vid = attr.VendorID, Pid = attr.ProductID };
                var sb = new StringBuilder(256);
                if (Native.HidD_GetProductString(h, sb, sb.Capacity * 2)) info.Product = sb.ToString().Trim();
                IntPtr pp;
                if (Native.HidD_GetPreparsedData(h, out pp))
                {
                    var caps = new Native.HIDP_CAPS();
                    Native.HidP_GetCaps(pp, ref caps);
                    info.UsagePage = caps.UsagePage; info.Usage = caps.Usage;
                    info.InLen = caps.InputReportByteLength; info.OutLen = caps.OutputReportByteLength;
                    info.FeatLen = caps.FeatureReportByteLength;
                    Native.HidD_FreePreparsedData(pp);
                }
                return info;
            }
        }
    }

    static class Native
    {
        public const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3, FILE_FLAG_OVERLAPPED = 0x40000000;
        public const int DIGCF_PRESENT = 2, DIGCF_DEVICEINTERFACE = 0x10;
        public const int HidP_Feature = 2;
        public const int HIDP_STATUS_SUCCESS = 0x00110000;

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential)]
        public struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID, ProductID, VersionNumber; }
        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
                NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
                NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        }
        // HIDP_VALUE_CAPS, 72 bytes. Usage/UsageMin share the union at offset 56.
        [StructLayout(LayoutKind.Explicit, Size = 72)]
        public struct HIDP_VALUE_CAPS
        {
            [FieldOffset(0)] public ushort UsagePage;
            [FieldOffset(2)] public byte ReportID;
            [FieldOffset(3)] public byte IsAlias;
            [FieldOffset(4)] public ushort BitField;
            [FieldOffset(6)] public ushort LinkCollection;
            [FieldOffset(8)] public ushort LinkUsage;
            [FieldOffset(10)] public ushort LinkUsagePage;
            [FieldOffset(12)] public byte IsRange;
            [FieldOffset(18)] public ushort BitSize;
            [FieldOffset(20)] public ushort ReportCount;
            [FieldOffset(56)] public ushort Usage;
            [FieldOffset(68)] public ushort DataIndex;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_DATA { public ushort DataIndex; public ushort Reserved; public uint RawValue; }

        [DllImport("hid.dll")] public static extern void HidD_GetHidGuid(out Guid g);
        [DllImport("hid.dll")] public static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
        [DllImport("hid.dll", CharSet = CharSet.Unicode)] public static extern bool HidD_GetProductString(SafeFileHandle h, StringBuilder sb, int len);
        [DllImport("hid.dll")] public static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
        [DllImport("hid.dll")] public static extern bool HidD_FreePreparsedData(IntPtr p);
        [DllImport("hid.dll")] public static extern bool HidD_GetFeature(SafeFileHandle h, byte[] buf, int len);
        [DllImport("hid.dll")] public static extern bool HidD_SetFeature(SafeFileHandle h, byte[] buf, int len);
        [DllImport("hid.dll")] public static extern int HidP_GetCaps(IntPtr p, ref HIDP_CAPS caps);
        [DllImport("hid.dll")] public static extern int HidP_GetValueCaps(int type, [Out] HIDP_VALUE_CAPS[] caps, ref ushort len, IntPtr p);
        [DllImport("hid.dll")] public static extern int HidP_SetData(int type, [In] HIDP_DATA[] data, ref uint len, IntPtr p, byte[] report, uint rlen);
        [DllImport("hid.dll")] public static extern int HidP_InitializeReportForID(int type, byte id, IntPtr p, byte[] report, uint len);
        [DllImport("hid.dll")] public static extern int HidP_SetUsageValue(int type, ushort page, ushort coll, ushort usage, uint value, IntPtr p, byte[] report, uint len);
        [DllImport("hid.dll")] public static extern int HidP_GetUsageValue(int type, ushort page, ushort coll, ushort usage, out uint value, IntPtr p, byte[] report, uint len);
        [DllImport("hid.dll")] public static extern int HidP_SetUsageValueArray(int type, ushort page, ushort coll, ushort usage, byte[] value, ushort vlen, IntPtr p, byte[] report, uint len);
        [DllImport("setupapi.dll", CharSet = CharSet.Auto)] public static extern IntPtr SetupDiGetClassDevs(ref Guid g, string e, IntPtr hwnd, int flags);
        [DllImport("setupapi.dll")] public static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr dev, ref Guid g, int idx, ref SP_DEVICE_INTERFACE_DATA d);
        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)] public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA d, IntPtr detail, int size, out int req, IntPtr devinfo);
        [DllImport("setupapi.dll")] public static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] public static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
        public const int ERROR_IO_PENDING = 997;
        [DllImport("kernel32.dll")] public static extern bool CancelIoEx(SafeFileHandle h, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool ReadFile(SafeFileHandle h, IntPtr buf, int len, IntPtr read, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool WriteFile(SafeFileHandle h, IntPtr buf, int len, IntPtr written, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GetOverlappedResult(SafeFileHandle h, IntPtr ov, out int done, bool wait);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr CreateEvent(IntPtr sec, bool manual, bool initial, string name);
        [DllImport("kernel32.dll")] public static extern uint WaitForSingleObject(IntPtr h, uint ms);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
    }
}
