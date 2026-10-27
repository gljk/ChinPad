using System.ComponentModel;
using System.Runtime.InteropServices;
using ChinPad.Native;
using static ChinPad.Native.RawInputNative;

namespace ChinPad;

/// <summary>
/// Hidden top-level window that owns the raw-input registration. Raw input arrives as WM_INPUT messages
/// sent to a window, so even the console diagnostic needs one plus a message loop (Application.Run).
/// RIDEV_INPUTSINK makes Windows deliver the reports while any other window has the focus, which is
/// what lets the diagnostic keep logging while the user is in Settings. The background mode also
/// registers its global pause/resume hotkey on this window (WM_HOTKEY), and asks for RIDEV_DEVNOTIFY so that
/// touchpads plugged in or removed are reported (WM_INPUT_DEVICE_CHANGE, 0.5.0); the diagnostic does not.
/// </summary>
internal sealed class InputWindow : NativeWindow, IDisposable
{
    const int WS_OVERLAPPED = 0x00000000;   // plain top-level window; no WS_VISIBLE, so it is never shown
    const int WS_EX_TOOLWINDOW = 0x00000080; // keep it out of Alt+Tab even if something showed it

    readonly int _headerSize = Marshal.SizeOf<RAWINPUTHEADER>();
    readonly int _hidHeaderSize = Marshal.SizeOf<RAWHID>();
    readonly List<RAWINPUTDEVICE> _registered = [];
    readonly HashSet<int> _hotkeys = [];
    byte[] _buffer = new byte[1024];

    /// <summary>
    /// Raised on the UI thread once per HID report: (raw-input device handle, report bytes). The bytes
    /// start with the report-ID byte, as RAWHID delivers them.
    /// </summary>
    public event Action<IntPtr, byte[]>? HidReport;

    /// <summary>Raised on the UI thread when a hotkey registered with RegisterHotKey is pressed (argument: its id).</summary>
    public event Action<int>? HotKey;

    /// <summary>
    /// Raised on the UI thread for WM_INPUT_DEVICE_CHANGE (registration with devNotify): true = a device of the
    /// registered usage arrived, false = one was removed; the second argument is its raw-input handle. Right
    /// after the registration Windows also reports every device that is already there as arrived.
    /// </summary>
    public event Action<bool, IntPtr>? DeviceChange;

    public long WmInputCount { get; private set; }

    public InputWindow()
    {
        CreateHandle(new CreateParams
        {
            Caption = "ChinPad raw input",
            Style = WS_OVERLAPPED,
            ExStyle = WS_EX_TOOLWINDOW,
        });
    }

    /// <summary>
    /// Asks Windows to send WM_INPUT for this top-level usage (all devices that have it) to this window;
    /// with <paramref name="devNotify"/> also WM_INPUT_DEVICE_CHANGE when such a device arrives or leaves.
    /// </summary>
    public void Register(ushort usagePage, ushort usage, bool devNotify = false)
    {
        var device = new RAWINPUTDEVICE
        {
            usUsagePage = usagePage,
            usUsage = usage,
            dwFlags = RIDEV_INPUTSINK | (devNotify ? RIDEV_DEVNOTIFY : 0),
            hwndTarget = Handle,
        };
        if (!RegisterRawInputDevices([device], 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"RegisterRawInputDevices(0x{usagePage:X2}/0x{usage:X2}) failed");
        _registered.Add(device);
    }

    public void Unregister()
    {
        if (_registered.Count == 0) return;
        RAWINPUTDEVICE[] remove = _registered
            .Select(r => new RAWINPUTDEVICE { usUsagePage = r.usUsagePage, usUsage = r.usUsage, dwFlags = RIDEV_REMOVE, hwndTarget = IntPtr.Zero })
            .ToArray();
        RegisterRawInputDevices(remove, (uint)remove.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
        _registered.Clear();
    }

    /// <summary>
    /// Registers a system-wide hotkey (MOD_NOREPEAT is added). Returns false with the Win32 error when it
    /// fails, typically 1409 ERROR_HOTKEY_ALREADY_REGISTERED: another program owns that combination.
    /// </summary>
    public bool RegisterHotKey(int id, uint modifiers, uint key, out int error)
    {
        UnregisterHotKey(id);
        error = 0;
        if (InputNative.RegisterHotKey(Handle, id, modifiers | InputNative.MOD_NOREPEAT, key))
        {
            _hotkeys.Add(id);
            return true;
        }
        error = Marshal.GetLastWin32Error();
        return false;
    }

    public void UnregisterHotKey(int id)
    {
        if (_hotkeys.Remove(id)) InputNative.UnregisterHotKey(Handle, id);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_INPUT)
        {
            WmInputCount++;
            ReadRawInput(m.LParam);
        }
        else if (m.Msg == InputNative.WM_HOTKEY)
        {
            HotKey?.Invoke((int)m.WParam);
            return;
        }
        else if (m.Msg == WM_INPUT_DEVICE_CHANGE)
        {
            int kind = (int)(long)m.WParam;
            if (kind is GIDC_ARRIVAL or GIDC_REMOVAL) DeviceChange?.Invoke(kind == GIDC_ARRIVAL, m.LParam);
        }
        // Always pass WM_INPUT on to DefWindowProc as well: the system frees the raw-input data there.
        base.WndProc(ref m);
    }

    /// <summary>
    /// WM_INPUT's lParam is an HRAWINPUT. GetRawInputData is called twice (size, then data). The block is
    /// RAWINPUTHEADER, then RAWHID {dwSizeHid, dwCount}, then dwCount reports of dwSizeHid bytes each.
    /// Several reports per message happen when the device sends faster than the message is processed.
    /// </summary>
    void ReadRawInput(IntPtr hRawInput)
    {
        uint size = 0;
        if (GetRawInputData(hRawInput, RID_INPUT, null, ref size, (uint)_headerSize) != 0 || size == 0) return;
        if (_buffer.Length < size) _buffer = new byte[Math.Max(size, (uint)_buffer.Length * 2)];

        uint got = GetRawInputData(hRawInput, RID_INPUT, _buffer, ref size, (uint)_headerSize);
        if (got == uint.MaxValue || got < _headerSize + _hidHeaderSize) return;

        RAWINPUTHEADER header = MemoryMarshal.Read<RAWINPUTHEADER>(_buffer);
        if (header.dwType != RIM_TYPEHID) return;

        RAWHID hid = MemoryMarshal.Read<RAWHID>(_buffer.AsSpan(_headerSize));
        int offset = _headerSize + _hidHeaderSize;
        for (uint i = 0; i < hid.dwCount; i++)
        {
            if (hid.dwSizeHid == 0 || offset + hid.dwSizeHid > got) break;
            var report = new byte[hid.dwSizeHid];
            Buffer.BlockCopy(_buffer, offset, report, 0, (int)hid.dwSizeHid);
            HidReport?.Invoke(header.hDevice, report);
            offset += (int)hid.dwSizeHid;
        }
    }

    public void Dispose()
    {
        foreach (int id in _hotkeys.ToList()) UnregisterHotKey(id);
        Unregister();
        if (Handle != IntPtr.Zero) DestroyHandle();
    }
}
