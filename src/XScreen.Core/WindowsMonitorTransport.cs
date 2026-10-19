using Microsoft.Win32;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace XScreen.Core;

public sealed class WindowsMonitorTransport : IMonitorTransport
{
    static long readAttempts, writeAttempts;
    public static long NativeReadAttempts => Interlocked.Read(ref readAttempts);
    public static long NativeWriteAttempts => Interlocked.Read(ref writeAttempts);
    sealed record Candidate(IntPtr Logical, MonitorDevice Device);
    public IReadOnlyList<MonitorDevice> Enumerate() => Candidates().Select(c => c.Device).ToArray();
    public IMonitorConnection Open(string deviceId)
    {
        var candidates = Candidates().Where(c => c.Device.Id == deviceId).ToArray();
        if (candidates.Length != 1) throw new InvalidOperationException("显示器已断开或连接发生变化，请刷新设备。");
        var candidate = candidates[0];
        if (!GetNumberOfPhysicalMonitorsFromHMONITOR(candidate.Logical, out uint count)) throw NativeError();
        if (count != 1) throw new InvalidOperationException("此显示器映射到多个物理设备，第一版暂不支持。");
        var physical = new Physical[count];
        if (!GetPhysicalMonitorsFromHMONITOR(candidate.Logical, count, physical)) throw NativeError();
        return new Connection(physical, candidate.Device.Supported);
    }
    static List<Candidate> Candidates()
    {
        var logical = new List<IntPtr>();
        EnumProc callback = (handle, _, _, _) => { logical.Add(handle); return true; };
        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero)) throw NativeError();
        GC.KeepAlive(callback);
        var result = new List<Candidate>();
        foreach (var handle in logical)
        {
            var info = new Info { Size = Marshal.SizeOf<Info>(), Device = "" };
            if (!GetMonitorInfo(handle, ref info)) continue;
            var display = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
            if (!EnumDisplayDevices(info.Device, 0, ref display, 1)) continue;
            var identity = display.DeviceId ?? "";
            byte[]? edid = null;
            var parts = identity.Split('#');
            if (parts.Length >= 3)
            {
                using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\DISPLAY\{parts[1]}\{parts[2]}\Device Parameters");
                edid = key?.GetValue("EDID") as byte[];
            }
            string name = EdidName(edid) ?? display.DeviceString ?? "显示器";
            string digest = edid is { Length: >= 128 } ? Convert.ToHexString(SHA256.HashData(edid)) : "no-edid";
            bool supported = parts.Length >= 3 && parts[1].Equals("GSM7845", StringComparison.OrdinalIgnoreCase) && digest != "no-edid";
            result.Add(new Candidate(handle, new MonitorDevice(identity + "|" + digest, name, info.Device, supported)));
        }
        return result;
    }
    static string? EdidName(byte[]? edid)
    {
        if (edid is not { Length: >= 128 }) return null;
        for (int offset = 54; offset + 18 <= 126; offset += 18)
            if (edid[offset] == 0 && edid[offset + 1] == 0 && edid[offset + 3] == 0xFC)
                return Encoding.ASCII.GetString(edid, offset + 5, 13).Trim('\0', '\r', '\n', ' ');
        return null;
    }
    static Win32Exception NativeError() => new(Marshal.GetLastWin32Error(), "无法与显示器通信，请检查连接和 DDC/CI 设置。");
    sealed class Connection(Physical[] handles, bool supported) : IMonitorConnection
    {
        bool disposed;
        public MonitorValue Read(byte code)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            Interlocked.Increment(ref readAttempts);
            bool ok = GetVCPFeatureAndVCPFeatureReply(handles[0].Handle, code, out _, out uint current, out uint maximum);
            return new MonitorValue(code, ok, ok ? current : 0, ok ? maximum : 0, ok ? null : Marshal.GetLastWin32Error());
        }
        public void Write(byte code, uint value)
        {
            MonitorSafetyPolicy.EnsureWriteAllowed(code);
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!supported || !(ControlSpec.Writable.Any(s => s.Code == code && value <= 100 && value % s.Step == 0)
                || OsdProtocol.IsValidEncodedWrite(code, value)))
                throw new InvalidOperationException("此设备或控制项不在第一版支持范围内。");
            Interlocked.Increment(ref writeAttempts);
            if (!SetVCPFeature(handles[0].Handle, code, value)) throw NativeError();
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (!DestroyPhysicalMonitors((uint)handles.Length, handles))
                System.Diagnostics.Trace.WriteLine("Physical monitor handle cleanup failed: " + Marshal.GetLastWin32Error());
        }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct Physical { public IntPtr Handle; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct Info { public int Size; public int Left, Top, Right, Bottom, WorkLeft, WorkTop, WorkRight, WorkBottom, Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }
    delegate bool EnumProc(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, EnumProc callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool GetMonitorInfo(IntPtr monitor, ref Info info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplayDevices(string name, uint index, ref DisplayDevice device, uint flags);
    [DllImport("dxva2.dll", SetLastError = true)] static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);
    [DllImport("dxva2.dll", SetLastError = true)] static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, [Out] Physical[] handles);
    [DllImport("dxva2.dll", SetLastError = true)] static extern bool DestroyPhysicalMonitors(uint count, Physical[] handles);
    [DllImport("dxva2.dll", SetLastError = true)] static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr monitor, byte code, out uint type, out uint current, out uint maximum);
    [DllImport("dxva2.dll", SetLastError = true)] static extern bool SetVCPFeature(IntPtr monitor, byte code, uint value);
}
