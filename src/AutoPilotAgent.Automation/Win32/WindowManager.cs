using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace AutoPilotAgent.Automation.Win32;

public sealed class WindowManager
{
    public bool FocusByTitleOrProcess(string? titleSubstring, string? processName)
    {
        var candidates = EnumerateTopLevelWindows();

        foreach (var w in candidates)
        {
            if (!string.IsNullOrWhiteSpace(processName) &&
                !string.Equals(w.ProcessName, processName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(titleSubstring) &&
                (w.Title?.Contains(titleSubstring, StringComparison.OrdinalIgnoreCase) != true))
            {
                continue;
            }

            return FocusWindow(w.Hwnd);
        }

        return false;
    }

    public (string? Title, string? ProcessName) GetForegroundWindowInfo()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return (null, null);
        }

        var title = GetWindowTitle(hwnd);
        var processName = GetProcessName(hwnd);
        return (title, processName);
    }

    public bool TryGetForegroundProcessName(out string processName)
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            processName = "";
            return false;
        }

        processName = GetProcessName(hwnd) ?? "";
        return !string.IsNullOrEmpty(processName);
    }

    public bool TryGetForegroundWindowRect(out (int Left, int Top, int Right, int Bottom) rect)
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            rect = default;
            return false;
        }

        if (!GetWindowRect(hwnd, out var r))
        {
            rect = default;
            return false;
        }

        rect = (r.Left, r.Top, r.Right, r.Bottom);
        return true;
    }

    public bool TryGetForegroundClientRectScreen(out (int Left, int Top, int Right, int Bottom) rect)
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            rect = default;
            return false;
        }

        if (!GetClientRect(hwnd, out var cr))
        {
            rect = default;
            return false;
        }

        var tl = new POINT { X = cr.Left, Y = cr.Top };
        var br = new POINT { X = cr.Right, Y = cr.Bottom };

        if (!ClientToScreen(hwnd, ref tl) || !ClientToScreen(hwnd, ref br))
        {
            rect = default;
            return false;
        }

        rect = (tl.X, tl.Y, br.X, br.Y);
        return true;
    }

    private static bool FocusWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        ShowWindow(hwnd, SW_RESTORE);
        return SetForegroundWindow(hwnd);
    }

    private static List<(IntPtr Hwnd, string? Title, string? ProcessName)> EnumerateTopLevelWindows()
    {
        var list = new List<(IntPtr, string?, string?)>();

        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
            {
                return true;
            }

            var title = GetWindowTitle(hwnd);
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            list.Add((hwnd, title, GetProcessName(hwnd)));
            return true;
        }, IntPtr.Zero);

        return list;
    }

    private static string? GetProcessName(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0)
        {
            return null;
        }

        try
        {
            return Process.GetProcessById((int)pid).ProcessName;
        }
        catch
        {
            return null;
        }
    }

    private static string? GetWindowTitle(IntPtr hwnd)
    {
        var len = GetWindowTextLength(hwnd);
        if (len <= 0)
        {
            return null;
        }

        var sb = new StringBuilder(len + 1);
        _ = GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private const int SW_RESTORE = 9;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
