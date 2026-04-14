using System.Runtime.InteropServices;

namespace WEMacs;

/// <summary>
/// CapsLock を F13 に変換して、既存のキーバインド処理へ流す。
/// </summary>
public sealed class CapsLockKeyHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeydown = 0x0100;
    private const int WmKeyup = 0x0101;
    private const int WmSyskeydown = 0x0104;
    private const int WmSyskeyup = 0x0105;
    private const int HcAction = 0;
    private const uint LlkhfUp = 0x80;

    private const int VkCapital = 0x14;
    private const ushort VkF13 = 0x7C;

    private const uint InputKeyboard = 1;
    private const uint KeyeventfKeyup = 0x0002;

    private IntPtr _hookId = IntPtr.Zero;
    private readonly LowLevelKeyboardProc _proc;

    public CapsLockKeyHook()
    {
        _proc = HookCallback;
    }

    public void Install()
    {
        if (_hookId != IntPtr.Zero)
            return;

        _hookId = SetWindowsHookEx(WhKeyboardLl, _proc, GetModuleHandle(null), 0);
        if (_hookId == IntPtr.Zero)
            throw new InvalidOperationException($"SetWindowsHookEx failed: {Marshal.GetLastWin32Error()}");
    }

    public void Uninstall()
    {
        if (_hookId == IntPtr.Zero)
            return;

        if (!UnhookWindowsHookEx(_hookId))
            throw new InvalidOperationException($"UnhookWindowsHookEx failed: {Marshal.GetLastWin32Error()}");

        _hookId = IntPtr.Zero;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == HcAction)
        {
            var info = Marshal.PtrToStructure<Kbdllhookstruct>(lParam);
            if (info.VkCode == VkCapital)
            {
                var msg = unchecked((uint)(nint)wParam);
                bool keyUp;
                if (msg == WmKeyup || msg == WmSyskeyup)
                    keyUp = true;
                else if (msg == WmKeydown || msg == WmSyskeydown)
                    keyUp = false;
                else
                    keyUp = (info.Flags & LlkhfUp) != 0;

                if (!TrySendF13(keyUp))
                    return CallNextHookEx(_hookId, nCode, wParam, lParam);

                return (IntPtr)1;
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static bool TrySendF13(bool keyUp)
    {
        var ki = new Keybdinput
        {
            WVk = VkF13,
            WScan = 0,
            DwFlags = keyUp ? KeyeventfKeyup : 0,
            Time = 0,
            DwExtraInfo = UIntPtr.Zero
        };

        var input = new Input { Type = InputKeyboard, Ki = ki };
        return SendInput(1, [input], Marshal.SizeOf<Input>()) == 1;
    }

    public void Dispose()
    {
        Uninstall();
        GC.SuppressFinalize(this);
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Explicit)]
    private struct Kbdllhookstruct
    {
        [FieldOffset(0)] public uint VkCode;
        [FieldOffset(4)] public uint ScanCode;
        [FieldOffset(8)] public uint Flags;
        [FieldOffset(12)] public uint Time;
        [FieldOffset(16)] public UIntPtr DwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public Keybdinput Ki;
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

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
