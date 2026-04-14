using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WEMacs;

public static class VirtualKeySender
{
    private const uint InputKeyboard = 1;
    private const uint KeyeventfKeyup = 0x0002;

    public static void Send(string sendKeys)
    {
        var inputs = new[]
        {
            CreateKeyboardInput(ParseSendKeys(sendKeys), keyUp: false),
            CreateKeyboardInput(ParseSendKeys(sendKeys), keyUp: true)
        };

        SendInputs(inputs);
    }

    public static void SendKeyDown(string sendKeys)
    {
        SendInputs([CreateKeyboardInput(ParseSendKeys(sendKeys), keyUp: false)]);
    }

    public static void SendKeyUp(string sendKeys)
    {
        SendInputs([CreateKeyboardInput(ParseSendKeys(sendKeys), keyUp: true)]);
    }

    private static ushort ParseSendKeys(string sendKeys)
    {
        if (string.IsNullOrWhiteSpace(sendKeys))
            throw new InvalidOperationException("sendKeys must not be empty.");

        var trimmed = sendKeys.Trim();
        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
        {
            var token = trimmed[1..^1];
            if (TryParseKeyToken(token, out var braceKey))
                return braceKey;
        }
        else if (trimmed.Length == 1 && TryParseKeyToken(trimmed, out var literalKey))
        {
            return literalKey;
        }

        throw new InvalidOperationException($"Unsupported sendKeys syntax: {sendKeys}");
    }

    private static bool TryParseKeyToken(string token, out ushort key)
    {
        var normalized = token.Trim().ToUpperInvariant() switch
        {
            "UP" => nameof(Keys.Up),
            "DOWN" => nameof(Keys.Down),
            "LEFT" => nameof(Keys.Left),
            "RIGHT" => nameof(Keys.Right),
            "ENTER" => nameof(Keys.Enter),
            "ESC" => nameof(Keys.Escape),
            "ESCAPE" => nameof(Keys.Escape),
            "PGUP" => nameof(Keys.PageUp),
            "PGDN" => nameof(Keys.PageDown),
            "DEL" => nameof(Keys.Delete),
            "INS" => nameof(Keys.Insert),
            _ => token.Trim()
        };

        if (Enum.TryParse<Keys>(normalized, ignoreCase: true, out var parsed))
        {
            key = (ushort)parsed;
            return true;
        }

        key = 0;
        return false;
    }

    private static void SendInputs(Input[] inputs)
    {
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }

    private static Input CreateKeyboardInput(ushort virtualKey, bool keyUp)
    {
        return new Input
        {
            Type = InputKeyboard,
            Union = new InputUnion
            {
                Keyboard = new Keybdinput
                {
                    WVk = virtualKey,
                    WScan = 0,
                    DwFlags = keyUp ? KeyeventfKeyup : 0,
                    Time = 0,
                    DwExtraInfo = UIntPtr.Zero
                }
            }
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public Mouseinput Mouse;

        [FieldOffset(0)]
        public Keybdinput Keyboard;

        [FieldOffset(0)]
        public Hardwareinput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Keybdinput
    {
        public ushort WVk;
        public ushort WScan;
        public uint DwFlags;
        public uint Time;
        public UIntPtr DwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Mouseinput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint DwFlags;
        public uint Time;
        public UIntPtr DwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Hardwareinput
    {
        public uint UMsg;
        public ushort WParamL;
        public ushort WParamH;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);
}
