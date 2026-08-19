using System.Runtime.InteropServices;

namespace WilkenAutomation.Worker.Windows;

/// <summary>
/// Captures the user's foreground window and keyboard focus, then restores them
/// after a UIA action. Does not minimize the automation window — if the user
/// restored it to watch, it stays visible.
/// </summary>
internal sealed class UserInputGuard : IDisposable
{
    private static int _generation;

    private readonly uint _automationPid;
    private readonly IntPtr _previousForeground;
    private readonly IntPtr _previousFocus;
    private readonly bool _userIsInspecting;
    private readonly int _token;

    public static UserInputGuard Capture(int automationProcessId) => new((uint)Math.Max(0, automationProcessId));

    private UserInputGuard(uint automationPid)
    {
        _automationPid = automationPid;
        _previousForeground = Native.GetForegroundWindow();
        _previousFocus = Native.GetFocusHwnd(_previousForeground);
        _userIsInspecting = Native.BelongsTo(_previousForeground, automationPid)
                            || Native.BelongsTo(_previousFocus, automationPid);
        _token = Interlocked.Increment(ref _generation);
        Native.LockSetForegroundWindow(Native.LsfwLock);
    }

    public void Dispose()
    {
        Native.LockSetForegroundWindow(Native.LsfwUnlock);
        if (_userIsInspecting) return;

        Native.Restore(_previousForeground, _previousFocus, _automationPid);

        // WPF often applies keyboard focus on the dispatcher after SetValue/Invoke returns.
        var fg = _previousForeground;
        var focus = _previousFocus;
        var pid = _automationPid;
        var token = _token;
        _ = Task.Run(async () =>
        {
            await Task.Delay(50).ConfigureAwait(false);
            if (Volatile.Read(ref _generation) != token) return;
            Native.Restore(fg, focus, pid);
        });
    }

    private static class Native
    {
        public const int LsfwLock = 1;
        public const int LsfwUnlock = 2;

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool GetGUIThreadInfo(uint idThread, ref GuiThreadInfo info);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool LockSetForegroundWindow(int uLockCode);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        private const uint GaRoot = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GuiThreadInfo
        {
            public int cbSize;
            public int flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public Rect rcCaret;
        }

        public static bool BelongsTo(IntPtr hwnd, uint pid)
        {
            if (hwnd == IntPtr.Zero || pid == 0) return false;
            GetWindowThreadProcessId(hwnd, out var windowPid);
            return windowPid == pid;
        }

        public static IntPtr GetFocusHwnd(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return IntPtr.Zero;
            var threadId = GetWindowThreadProcessId(hwnd, out _);
            var info = new GuiThreadInfo { cbSize = Marshal.SizeOf<GuiThreadInfo>() };
            if (!GetGUIThreadInfo(threadId, ref info)) return hwnd;
            if (info.hwndFocus != IntPtr.Zero) return info.hwndFocus;
            return info.hwndActive != IntPtr.Zero ? info.hwndActive : hwnd;
        }

        public static void Restore(IntPtr previousForeground, IntPtr previousFocus, uint automationPid)
        {
            try
            {
                var currentFg = GetForegroundWindow();
                var stolenForeground = BelongsTo(currentFg, automationPid)
                    && previousForeground != IntPtr.Zero
                    && IsWindow(previousForeground)
                    && !BelongsTo(previousForeground, automationPid);

                if (stolenForeground)
                    ForceForeground(RootWindow(previousForeground));

                var target = previousFocus != IntPtr.Zero && IsWindow(previousFocus)
                    ? previousFocus
                    : previousForeground;
                if (target == IntPtr.Zero || !IsWindow(target) || BelongsTo(target, automationPid))
                    return;

                var currentFocus = GetFocusHwnd(GetForegroundWindow());
                var stolenFocus = BelongsTo(currentFocus, automationPid) || stolenForeground;
                if (!stolenFocus || currentFocus == target) return;

                ForceFocus(target);
            }
            catch
            {
                // Never fail a job because of z-order / focus restoration.
            }
        }

        private static IntPtr RootWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return hwnd;
            var root = GetAncestor(hwnd, GaRoot);
            return root != IntPtr.Zero ? root : hwnd;
        }

        private static void ForceForeground(IntPtr hwnd)
        {
            var current = GetForegroundWindow();
            if (current == hwnd) return;
            WithAttachedThreads(current, hwnd, () => SetForegroundWindow(hwnd));
        }

        private static void ForceFocus(IntPtr hwnd)
        {
            var current = GetForegroundWindow();
            var root = RootWindow(hwnd);
            WithAttachedThreads(current, hwnd, () =>
            {
                SetForegroundWindow(root);
                SetFocus(hwnd);
            });
        }

        private static void WithAttachedThreads(IntPtr fromHwnd, IntPtr toHwnd, Action action)
        {
            var thisThread = GetCurrentThreadId();
            var fromThread = fromHwnd == IntPtr.Zero ? 0 : GetWindowThreadProcessId(fromHwnd, out _);
            var toThread = toHwnd == IntPtr.Zero ? 0 : GetWindowThreadProcessId(toHwnd, out _);

            var attachedFrom = fromThread != 0 && fromThread != thisThread && AttachThreadInput(thisThread, fromThread, true);
            var attachedTo = toThread != 0 && toThread != thisThread && toThread != fromThread && AttachThreadInput(thisThread, toThread, true);
            try
            {
                action();
            }
            finally
            {
                if (attachedFrom) AttachThreadInput(thisThread, fromThread, false);
                if (attachedTo) AttachThreadInput(thisThread, toThread, false);
            }
        }
    }
}
