using System.Runtime.InteropServices;

namespace ChinPad.Native;

/// <summary>
/// user32 / kernel32 functions for the background mode: SendInput (cursor moves and clicks), GetCursorPos
/// and the virtual-screen metrics (absolute moves), RegisterHotKey (pause/resume), the low-level mouse
/// hook (strategy 3, hookClicks) with the message-loop functions its thread needs, DestroyIcon (tray icon),
/// GetDoubleClickTime (blockDoubleClick) and the per-thread DPI context. Sizes in comments are for a 64-bit process (ChinPad is win-x64 only).
/// </summary>
internal static class InputNative
{
    /// <summary>
    /// dwExtraInfo of every input ChinPad injects ('CHNP'). The hook recognises ChinPad's own moves and
    /// clicks by it; any other program can read it too, which is harmless.
    /// </summary>
    public static readonly IntPtr ChinPadTag = new(0x43484E50);

    // INPUT.type and MOUSEINPUT.dwFlags
    public const uint INPUT_MOUSE = 0;
    public const uint MOUSEEVENTF_MOVE = 0x0001;
    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP = 0x0004;
    public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    public const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    public const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    public const uint MOUSEEVENTF_VIRTUALDESK = 0x4000; // with ABSOLUTE: 0..65535 spans the whole virtual screen
    public const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

    // GetSystemMetrics: bounding rectangle of all monitors, in pixels
    public const int SM_XVIRTUALSCREEN = 76;
    public const int SM_YVIRTUALSCREEN = 77;
    public const int SM_CXVIRTUALSCREEN = 78;
    public const int SM_CYVIRTUALSCREEN = 79;

    // RegisterHotKey modifiers and the message it posts
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000; // holding the keys down does not repeat WM_HOTKEY
    public const int WM_HOTKEY = 0x0312;

    // Low-level mouse hook
    public const int WH_MOUSE_LL = 14;
    public const int WM_MOUSEMOVE = 0x0200;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_RBUTTONDOWN = 0x0204;
    public const int WM_RBUTTONUP = 0x0205;
    public const int WM_MBUTTONDOWN = 0x0207;
    public const int WM_MBUTTONUP = 0x0208;
    public const uint LLMHF_INJECTED = 0x00000001;

    // Message loop of the hook thread
    public const uint WM_QUIT = 0x0012;
    public const uint PM_NOREMOVE = 0x0000;

    /// <summary>DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2: coordinates in physical pixels on every monitor.</summary>
    public static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new(-4);

    /// <summary>MOUSEINPUT, 32 bytes on x64 (4 bytes padding before dwExtraInfo).</summary>
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct MOUSEINPUT
    {
        [FieldOffset(0)] public int dx;
        [FieldOffset(4)] public int dy;
        [FieldOffset(8)] public uint mouseData;
        [FieldOffset(12)] public uint dwFlags;
        [FieldOffset(16)] public uint time;
        [FieldOffset(24)] public IntPtr dwExtraInfo;
    }

    /// <summary>
    /// INPUT, 40 bytes on x64: type, 4 bytes padding, then the union of MOUSEINPUT (32 bytes, the largest),
    /// KEYBDINPUT and HARDWAREINPUT. Only the mouse variant is declared.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    public struct INPUT
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    /// <summary>MSLLHOOKSTRUCT, 32 bytes on x64: what the low-level mouse hook receives in lParam.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;       // LLMHF_INJECTED etc.
        public uint time;
        public IntPtr dwExtraInfo;
    }

    /// <summary>MSG, 48 bytes on x64.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
        public uint lPrivate;
    }

    public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    /// <summary>Returns the number of inputs inserted. Input blocked by UIPI (elevated foreground window) is NOT reported as a failure.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    /// <summary>Returns &gt; 0 for a message, 0 for WM_QUIT, -1 on error.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool TranslateMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>Windows' double-click time in ms (Control Panel > Mouse; 500 by default). blockDoubleClick with doubleClickWindowMs 0 uses it.</summary>
    [DllImport("user32.dll")]
    public static extern uint GetDoubleClickTime();

    /// <summary>Returns the previous context, or IntPtr.Zero on failure (Windows older than 10 1607).</summary>
    [DllImport("user32.dll")]
    public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

    // Display scaling, for the diagnostic bundle's system.txt.
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const int MDT_EFFECTIVE_DPI = 0;

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    /// <summary>shcore.dll, Windows 8.1 and later. Returns an HRESULT (0 = S_OK).</summary>
    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    /// <summary>One-line check that the structs above have the native x64 sizes (logged at start-up).</summary>
    public static string DescribeLayout(out bool ok)
    {
        int input = Marshal.SizeOf<INPUT>(), mouse = Marshal.SizeOf<MOUSEINPUT>(), hook = Marshal.SizeOf<MSLLHOOKSTRUCT>();
        ok = !Environment.Is64BitProcess || (input == 40 && mouse == 32 && hook == 32);
        return $"struct sizes {(ok ? "OK" : "MISMATCH")}: INPUT {input}, MOUSEINPUT {mouse}, MSLLHOOKSTRUCT {hook} (expected 40, 32, 32 on x64)";
    }
}

/// <summary>
/// Moves the real cursor and presses its buttons with SendInput, every input tagged with InputNative.ChinPadTag.
///  - relative (default): MOUSEEVENTF_MOVE with dx/dy. Windows applies the pointer speed and, if
///    "Enhance pointer precision" is on, its acceleration curve on top of ChinPad's.
///  - absolute: GetCursorPos + delta, clamped to the virtual screen and sent as
///    MOUSEEVENTF_MOVE|ABSOLUTE|VIRTUALDESK, where 0..65535 spans the virtual screen. This bypasses the OS
///    ballistics, so one pixel from the engine is one pixel on screen.
/// Buttons: MOUSEEVENTF_LEFTDOWN / LEFTUP etc. without MOVE, so the click happens where the cursor is.
/// SendInput cannot move the cursor over windows of elevated programs or on the UAC desktop (UIPI), nor
/// click there, and it does not report that as an error.
/// </summary>
internal sealed class Win32Mover : IMover
{
    static readonly int InputSize = Marshal.SizeOf<InputNative.INPUT>();
    readonly InputNative.INPUT[] _input = new InputNative.INPUT[1];

    public bool Absolute { get; set; }
    public long Failures { get; private set; }
    public int LastError { get; private set; }

    public void Move(int dx, int dy)
    {
        if (Absolute) MoveAbsolute(dx, dy);
        else Send(dx, dy, InputNative.MOUSEEVENTF_MOVE);
    }

    public void Button(MouseButton button, bool down) => Send(0, 0, button switch
    {
        MouseButton.Left => down ? InputNative.MOUSEEVENTF_LEFTDOWN : InputNative.MOUSEEVENTF_LEFTUP,
        MouseButton.Right => down ? InputNative.MOUSEEVENTF_RIGHTDOWN : InputNative.MOUSEEVENTF_RIGHTUP,
        _ => down ? InputNative.MOUSEEVENTF_MIDDLEDOWN : InputNative.MOUSEEVENTF_MIDDLEUP,
    });

    void MoveAbsolute(int dx, int dy)
    {
        // Per-monitor-aware for these calls, so that cursor position and virtual-screen size are both in
        // physical pixels on every monitor (the laptop panel runs at 200 %, the external one does not).
        IntPtr previous = InputNative.SetThreadDpiAwarenessContext(InputNative.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        try
        {
            if (!InputNative.GetCursorPos(out InputNative.POINT p))
            {
                Failures++;
                LastError = Marshal.GetLastWin32Error();
                return;
            }
            int vx = InputNative.GetSystemMetrics(InputNative.SM_XVIRTUALSCREEN);
            int vy = InputNative.GetSystemMetrics(InputNative.SM_YVIRTUALSCREEN);
            int vw = InputNative.GetSystemMetrics(InputNative.SM_CXVIRTUALSCREEN);
            int vh = InputNative.GetSystemMetrics(InputNative.SM_CYVIRTUALSCREEN);
            if (vw < 2 || vh < 2) return;
            int x = Math.Clamp(p.X + dx, vx, vx + vw - 1);
            int y = Math.Clamp(p.Y + dy, vy, vy + vh - 1);
            Send(ToNormalized(x - vx, vw), ToNormalized(y - vy, vh),
                InputNative.MOUSEEVENTF_MOVE | InputNative.MOUSEEVENTF_ABSOLUTE | InputNative.MOUSEEVENTF_VIRTUALDESK);
        }
        finally
        {
            if (previous != IntPtr.Zero) InputNative.SetThreadDpiAwarenessContext(previous);
        }
    }

    /// <summary>
    /// Pixel offset 0..size-1 inside the virtual screen -> 0..65535. Windows maps back with
    /// pixel = floor(n * size / 65536), so the smallest n that lands on the pixel is ceil(offset * 65536 / size).
    /// (The common round(offset * 65535 / (size - 1)) was measured one pixel short near the left edge here.)
    /// </summary>
    public static int ToNormalized(int offset, int size) =>
        (int)Math.Min(65535, ((long)offset * 65536 + size - 1) / size);

    void Send(int dx, int dy, uint flags)
    {
        _input[0] = new InputNative.INPUT
        {
            type = InputNative.INPUT_MOUSE,
            mi = new InputNative.MOUSEINPUT { dx = dx, dy = dy, dwFlags = flags, dwExtraInfo = InputNative.ChinPadTag },
        };
        if (InputNative.SendInput(1, _input, InputSize) != 1)
        {
            Failures++;
            LastError = Marshal.GetLastWin32Error();
        }
    }
}
