using System.Runtime.InteropServices;

namespace ChinPad.Native;

/// <summary>
/// user32 Raw Input API.
/// Raw input hands the untouched HID input reports of a device to a window (WM_INPUT), independent of
/// what Windows' touchpad stack (palm rejection, gestures) later does with them. Every HID top-level
/// collection (TLC) is its own raw-input "device": the touchpad's Precision Touchpad TLC (usage page
/// 0x0D, usage 0x05) is one, its mouse TLC is another, and so on.
/// Sizes in comments are for a 64-bit process (ChinPad is published for win-x64 only).
/// </summary>
internal static class RawInputNative
{
    // dwType values used by RAWINPUTDEVICELIST, RAWINPUTHEADER and RID_DEVICE_INFO.
    public const uint RIM_TYPEMOUSE = 0;
    public const uint RIM_TYPEKEYBOARD = 1;
    public const uint RIM_TYPEHID = 2;

    // GetRawInputDeviceInfo uiCommand values.
    public const uint RIDI_PREPARSEDDATA = 0x20000005; // HID preparsed data (opaque blob for HidP_*), size in bytes
    public const uint RIDI_DEVICENAME = 0x20000007;    // device interface path; size is in CHARACTERS
    public const uint RIDI_DEVICEINFO = 0x2000000b;    // RID_DEVICE_INFO; the caller sets cbSize

    // GetRawInputData uiCommand: copy the header plus the device data.
    public const uint RID_INPUT = 0x10000003;

    // RAWINPUTDEVICE.dwFlags.
    public const uint RIDEV_REMOVE = 0x00000001;     // unregister this usage page/usage (hwndTarget must be NULL)
    public const uint RIDEV_INPUTSINK = 0x00000100;  // keep receiving input while another window has the focus
    public const uint RIDEV_DEVNOTIFY = 0x00002000;  // also send WM_INPUT_DEVICE_CHANGE when a device of this usage arrives or leaves

    public const int WM_INPUT = 0x00FF;
    /// <summary>A device of a registered usage arrived (wParam GIDC_ARRIVAL) or was removed (GIDC_REMOVAL); lParam = its raw-input handle.</summary>
    public const int WM_INPUT_DEVICE_CHANGE = 0x00FE;
    public const int GIDC_ARRIVAL = 1;
    public const int GIDC_REMOVAL = 2;

    /// <summary>One entry of GetRawInputDeviceList (16 bytes: handle, type, padding).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTDEVICELIST
    {
        public IntPtr hDevice;
        public uint dwType;
    }

    /// <summary>Registration request "send WM_INPUT for this usage page/usage to hwndTarget" (16 bytes).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    /// <summary>Header at the start of every RAWINPUT block (24 bytes). hDevice says which TLC sent it.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    /// <summary>
    /// HID part of RAWINPUT, directly after the header (8 bytes). It is followed by dwCount reports of
    /// dwSizeHid bytes each (bRawData). Every report starts with its report-ID byte (0 when the device
    /// does not use report IDs), which is exactly the buffer layout the HidP_* functions expect.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RAWHID
    {
        public uint dwSizeHid;
        public uint dwCount;
    }

    /// <summary>
    /// RID_DEVICE_INFO: cbSize, dwType, then a union of the mouse / keyboard / HID variants. The keyboard
    /// variant is the largest (6 DWORDs = 24 bytes), so the struct is 8 + 24 = 32 bytes. Only the HID
    /// variant (RID_DEVICE_INFO_HID) is declared, at the union offset 8.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct RID_DEVICE_INFO
    {
        [FieldOffset(0)] public uint cbSize;
        [FieldOffset(4)] public uint dwType;
        [FieldOffset(8)] public uint hidVendorId;
        [FieldOffset(12)] public uint hidProductId;
        [FieldOffset(16)] public uint hidVersionNumber;
        [FieldOffset(20)] public ushort hidUsagePage;
        [FieldOffset(22)] public ushort hidUsage;
    }

    /// <summary>Returns the number of entries copied, or uint.MaxValue on error. Call with null first to get the count.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetRawInputDeviceList([Out] RAWINPUTDEVICELIST[]? pRawInputDeviceList, ref uint puiNumDevices, uint cbSize);

    /// <summary>With pData = IntPtr.Zero: returns 0 and the required size in pcbSize. Otherwise: bytes/chars copied, or uint.MaxValue.</summary>
    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetRawInputDeviceInfoW")]
    public static extern uint GetRawInputDeviceInfo(IntPtr hDevice, uint uiCommand, IntPtr pData, ref uint pcbSize);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetRawInputDeviceInfoW")]
    public static extern uint GetRawInputDeviceInfo(IntPtr hDevice, uint uiCommand, ref RID_DEVICE_INFO pData, ref uint pcbSize);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterRawInputDevices([In] RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

    /// <summary>With pData = null: returns 0 and the required size. Otherwise: bytes copied, or uint.MaxValue.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, [Out] byte[]? pData, ref uint pcbSize, uint cbSizeHeader);
}

/// <summary>
/// kernel32 console functions. ChinPad is a WinExe (the later tray mode must not flash a console window),
/// so the console modes have to connect a console themselves before the first Console call.
/// </summary>
internal static class ConsoleNative
{
    public const int ATTACH_PARENT_PROCESS = -1;
    public const int STD_INPUT_HANDLE = -10;
    public const int STD_OUTPUT_HANDLE = -11;
    public const int STD_ERROR_HANDLE = -12;

    public const uint FILE_TYPE_UNKNOWN = 0; // also returned for NULL or invalid handles
    public const uint FILE_TYPE_DISK = 1;
    public const uint FILE_TYPE_CHAR = 2;    // console
    public const uint FILE_TYPE_PIPE = 3;

    public const uint GENERIC_READ = 0x80000000;
    public const uint GENERIC_WRITE = 0x40000000;
    public const uint FILE_SHARE_READ = 0x1;
    public const uint FILE_SHARE_WRITE = 0x2;
    public const uint OPEN_EXISTING = 3;
    public static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetStdHandle(int nStdHandle, IntPtr hHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint GetFileType(IntPtr hFile);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
        uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WriteConsoleW(IntPtr hConsoleOutput, string lpBuffer, uint nNumberOfCharsToWrite,
        out uint lpNumberOfCharsWritten, IntPtr lpReserved);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr hObject);
}
