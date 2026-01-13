using System.Runtime.InteropServices;
using System.Text;

namespace AutoPilotAgent.Automation.Win32;

public sealed class InputSender
{
    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;

    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;

    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    public void ClickAbsolute(int x, int y)
    {
        MoveMouseAbsolute(x, y);
        SendMouse(MOUSEEVENTF_LEFTDOWN);
        SendMouse(MOUSEEVENTF_LEFTUP);
    }

    public void LeftDown()
    {
        SendMouse(MOUSEEVENTF_LEFTDOWN);
    }

    public void LeftUp()
    {
        SendMouse(MOUSEEVENTF_LEFTUP);
    }

    public void DragAbsolute(int fromX, int fromY, int toX, int toY, int steps = 24, int stepDelayMs = 5)
    {
        steps = Math.Clamp(steps, 1, 200);
        stepDelayMs = Math.Clamp(stepDelayMs, 0, 50);

        MoveMouseAbsolute(fromX, fromY);
        Thread.Sleep(10);
        LeftDown();
        Thread.Sleep(10);

        for (var i = 1; i <= steps; i++)
        {
            var t = i / (double)steps;
            var x = (int)Math.Round(fromX + (toX - fromX) * t);
            var y = (int)Math.Round(fromY + (toY - fromY) * t);
            MoveMouseAbsolute(x, y);
            if (stepDelayMs > 0)
            {
                Thread.Sleep(stepDelayMs);
            }
        }

        Thread.Sleep(10);
        LeftUp();
        Thread.Sleep(10);
    }

    public void MoveMouseAbsolute(int x, int y)
    {
        var (ax, ay) = NormalizeToAbsolute(x, y);
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = ax,
                    dy = ay,
                    mouseData = 0,
                    dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    public void KeyDown(ushort vk)
    {
        SendKey(vk, 0);
    }

    public void KeyUp(ushort vk)
    {
        SendKey(vk, KEYEVENTF_KEYUP);
    }

    public void Hotkey(params ushort[] keys)
    {
        foreach (var k in keys)
        {
            KeyDown(k);
        }

        for (var i = keys.Length - 1; i >= 0; i--)
        {
            KeyUp(keys[i]);
        }
    }

    public void TypeText(string text)
    {
        foreach (var ch in text)
        {
            SendUnicodeChar(ch);
        }
    }

    public void MouseWheel(int delta)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = 0,
                    dy = 0,
                    mouseData = unchecked((uint)delta),
                    dwFlags = MOUSEEVENTF_WHEEL,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private void SendUnicodeChar(char c)
    {
        var down = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = c,
                    dwFlags = KEYEVENTF_UNICODE,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        var up = down;
        up.U.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;

        SendInput(2, new[] { down, up }, Marshal.SizeOf<INPUT>());
    }

    private void SendMouse(uint flags)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = 0,
                    dy = 0,
                    mouseData = 0,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private void SendKey(ushort vk, uint flags)
    {
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private static (int ax, int ay) NormalizeToAbsolute(int x, int y)
    {
        // SendInput expects 0..65535 across the virtual screen.
        var vs = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        var hs = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        var left = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var top = GetSystemMetrics(SM_YVIRTUALSCREEN);

        var ax = (int)Math.Round((x - left) * 65535.0 / Math.Max(1, vs - 1));
        var ay = (int)Math.Round((y - top) * 65535.0 / Math.Max(1, hs - 1));

        ax = Math.Clamp(ax, 0, 65535);
        ay = Math.Clamp(ay, 0, 65535);

        return (ax, ay);
    }

    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;
        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
}
