namespace AutoPilotAgent.Automation.Win32;

public static class VirtualKey
{
    public const ushort LWIN = 0x5B;
    public const ushort RWIN = 0x5C;
    public const ushort SHIFT = 0x10;
    public const ushort CONTROL = 0x11;
    public const ushort MENU = 0x12; // ALT

    public const ushort RETURN = 0x0D;
    public const ushort TAB = 0x09;
    public const ushort ESCAPE = 0x1B;

    public const ushort LEFT = 0x25;
    public const ushort UP = 0x26;
    public const ushort RIGHT = 0x27;
    public const ushort DOWN = 0x28;

    public static ushort FromName(string key)
    {
        var k = key.Trim().ToUpperInvariant();
        return k switch
        {
            "WIN" or "LWIN" => LWIN,
            "RWIN" => RWIN,
            "SHIFT" => SHIFT,
            "CTRL" or "CONTROL" => CONTROL,
            "ALT" => MENU,
            "ENTER" or "RETURN" => RETURN,
            "TAB" => TAB,
            "ESC" or "ESCAPE" => ESCAPE,
            "LEFT" => LEFT,
            "UP" => UP,
            "RIGHT" => RIGHT,
            "DOWN" => DOWN,
            _ when k.Length == 1 && char.IsLetterOrDigit(k[0]) => (ushort)char.ToUpperInvariant(k[0]),
            _ => throw new ArgumentOutOfRangeException(nameof(key), $"Unsupported key: {key}")
        };
    }
}
