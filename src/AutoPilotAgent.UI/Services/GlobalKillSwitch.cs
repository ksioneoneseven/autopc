using System.Runtime.InteropServices;

namespace AutoPilotAgent.UI.Services;

public sealed class GlobalKillSwitch : IDisposable
{
    private readonly Action _onKill;
    private readonly object _gate = new();

    private IntPtr _hook;
    private LowLevelKeyboardProc? _proc;
    private DateTime _lastEscUtc = DateTime.MinValue;

    public GlobalKillSwitch(Action onKill)
    {
        _onKill = onKill;
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_hook != IntPtr.Zero)
            {
                return;
            }

            _proc = HookCallback;
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to install keyboard hook.");
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }

            _proc = null;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            var vkCode = Marshal.ReadInt32(lParam);
            if (vkCode == VK_ESCAPE)
            {
                var now = DateTime.UtcNow;
                var prev = _lastEscUtc;
                _lastEscUtc = now;

                if ((now - prev) <= TimeSpan.FromMilliseconds(500))
                {
                    try
                    {
                        _onKill();
                    }
                    catch
                    {
                    }
                }
            }
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int VK_ESCAPE = 0x1B;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
