using System.Runtime.InteropServices;

namespace XScreen.App;

static class SessionEnvironment
{
    // Read the current process session only. No display, DDC/CI or remote-host access.
    internal static bool IsRemoteDesktop()
    {
        IntPtr buffer = IntPtr.Zero;
        try
        {
            // WTSClientProtocolType: USHORT 0 = console, 2 = RDP.
            return WTSQuerySessionInformation(IntPtr.Zero, -1, 16, out buffer, out uint bytes)
                && buffer != IntPtr.Zero && bytes >= sizeof(ushort) && Marshal.ReadInt16(buffer) == 2;
        }
        finally { if (buffer != IntPtr.Zero) WTSFreeMemory(buffer); }
    }
    [DllImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool WTSQuerySessionInformation(IntPtr server, int sessionId, int infoClass, out IntPtr buffer, out uint bytes);
    [DllImport("wtsapi32.dll", ExactSpelling = true)] static extern void WTSFreeMemory(IntPtr buffer);
}
