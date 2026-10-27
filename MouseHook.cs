using System.ComponentModel;
using System.Runtime.InteropServices;
using static ChinPad.Native.InputNative;

namespace ChinPad;

/// <summary>
/// A low-level mouse hook (WH_MOUSE_LL) that keeps Windows' own touchpad handling out of ChinPad's way:
///  - strategy 3: drops Windows' cursor moves while the touchpad reports a contact Windows acts on, so
///    that only ChinPad moves the cursor (blockMoves);
///  - hookClicks: drops Windows' left/right/middle button events while ChinPad owns a touch, so that a
///    chin press is not clicked twice or in the wrong place (blockClicks).
///
/// The hook lives on its own thread with its own message loop: Windows calls a low-level hook from inside
/// the installing thread's GetMessage, and the UI thread is busy with raw input and SendInput (calling
/// SendInput on the hook's own thread would wait on itself). The callback must stay trivial: if it
/// takes longer than LowLevelHooksTimeout, Windows skips it and eventually removes the hook silently.
///
/// Rules: input injected by ChinPad (LLMHF_INJECTED and our dwExtraInfo tag) always passes; any other
/// WM_MOUSEMOVE is dropped while blockMoves() is true; any other button DOWN (left, right, middle) is
/// dropped while blockClicks() is true; wheel and X buttons always pass.
/// Stuck-button rule: per button, an UP follows the fate of its DOWN, whatever blockClicks() says when
/// the UP arrives. A DOWN that was let through gets its UP (otherwise the button would stay down in
/// Windows); a DOWN that was dropped has its UP dropped too. An UP without a DOWN seen passes.
///
/// Limitation: a hook in a non-elevated process does not see input that goes to elevated windows (UIPI),
/// so over an administrator window Windows' moves and clicks are not blocked (and ChinPad's SendInput
/// cannot move the cursor or click there either).
/// </summary>
internal sealed class MouseHook : IDisposable
{
    static readonly int FlagsOffset = (int)Marshal.OffsetOf<MSLLHOOKSTRUCT>(nameof(MSLLHOOKSTRUCT.flags));
    static readonly int ExtraInfoOffset = (int)Marshal.OffsetOf<MSLLHOOKSTRUCT>(nameof(MSLLHOOKSTRUCT.dwExtraInfo));

    readonly Func<bool> _blockMoves;
    readonly Func<bool>? _blockClicks;
    readonly LowLevelMouseProc _proc; // kept in a field so the GC cannot collect the delegate Windows calls
    readonly bool[] _downDropped = new bool[3]; // left, right, middle: was the last foreign DOWN dropped? (hook thread only)
    Thread? _thread;
    uint _threadId;
    IntPtr _hook;
    long _dropped, _droppedClicks;

    /// <param name="blockMoves">Called on the hook thread for every foreign mouse move; must be fast and thread-safe.</param>
    /// <param name="blockClicks">Called on the hook thread for every foreign button DOWN; must be fast and thread-safe. Null: clicks always pass.</param>
    public MouseHook(Func<bool> blockMoves, Func<bool>? blockClicks = null)
    {
        _blockMoves = blockMoves;
        _blockClicks = blockClicks;
        _proc = Callback;
    }

    public bool Running => _thread is not null;
    /// <summary>Foreign mouse moves dropped so far.</summary>
    public long Dropped => Interlocked.Read(ref _dropped);
    /// <summary>Foreign button presses (DOWN events; their UPs were dropped with them) dropped so far.</summary>
    public long DroppedClicks => Interlocked.Read(ref _droppedClicks);

    /// <summary>Starts the hook thread and waits until the hook is installed or has failed.</summary>
    public bool Start(out string error)
    {
        error = "";
        if (_thread is not null) return true;
        Array.Clear(_downDropped); // a DOWN dropped before an earlier Stop must not take a later UP with it
        using var ready = new ManualResetEventSlim();
        int win32Error = 0;
        var thread = new Thread(() =>
        {
            PeekMessage(out _, IntPtr.Zero, 0, 0, PM_NOREMOVE); // make sure this thread has a message queue
            _threadId = GetCurrentThreadId();
            _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero) win32Error = Marshal.GetLastWin32Error();
            ready.Set();
            if (_hook == IntPtr.Zero) return;

            while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0) // the callback runs in here; WM_QUIT ends it
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        })
        {
            IsBackground = true,
            Name = "ChinPad mouse hook",
            Priority = ThreadPriority.AboveNormal,
        };
        thread.Start();
        ready.Wait();
        if (_hook == IntPtr.Zero)
        {
            thread.Join();
            error = $"SetWindowsHookEx(WH_MOUSE_LL) failed: {new Win32Exception(win32Error).Message} (error {win32Error})";
            return false;
        }
        _thread = thread;
        return true;
    }

    public void Stop()
    {
        if (_thread is null) return;
        PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(2000);
        _thread = null;
    }

    public void Dispose() => Stop();

    IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        int msg = (int)wParam;
        (int button, bool down) = msg switch
        {
            WM_LBUTTONDOWN => (0, true),
            WM_LBUTTONUP => (0, false),
            WM_RBUTTONDOWN => (1, true),
            WM_RBUTTONUP => (1, false),
            WM_MBUTTONDOWN => (2, true),
            WM_MBUTTONUP => (2, false),
            _ => (-1, false),
        };
        if (nCode >= 0 && (msg == WM_MOUSEMOVE || button >= 0))
        {
            uint flags = (uint)Marshal.ReadInt32(lParam, FlagsOffset);
            bool ours = (flags & LLMHF_INJECTED) != 0 && Marshal.ReadIntPtr(lParam, ExtraInfoOffset) == ChinPadTag;
            if (!ours && Drop(msg, button, down))
                return 1; // non-zero: Windows discards this event
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    /// <summary>Decides for one foreign move or button event (hook thread only).</summary>
    bool Drop(int msg, int button, bool down)
    {
        if (msg == WM_MOUSEMOVE)
        {
            if (!_blockMoves()) return false;
            Interlocked.Increment(ref _dropped);
            return true;
        }
        if (down)
        {
            bool drop = _blockClicks?.Invoke() ?? false;
            _downDropped[button] = drop;
            if (drop) Interlocked.Increment(ref _droppedClicks);
            return drop;
        }
        // UP: same fate as its DOWN (stuck-button rule).
        bool dropUp = _downDropped[button];
        _downDropped[button] = false;
        return dropUp;
    }
}
