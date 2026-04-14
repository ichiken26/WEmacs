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
    private const int VkEisu = 0xF0;
    private const ushort VkF13 = 0x7C;

    private const uint InputKeyboard = 1;
    private const uint KeyeventfKeyup = 0x0002;

    private IntPtr _hookId = IntPtr.Zero;
    private readonly LowLevelKeyboardProc _proc;
    private bool _isF13Down;

    public event EventHandler<bool>? F13StateChanged;

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

        EnsureF13Released();

        if (!UnhookWindowsHookEx(_hookId))
            throw new InvalidOperationException($"UnhookWindowsHookEx failed: {Marshal.GetLastWin32Error()}");

        _hookId = IntPtr.Zero;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == HcAction)
        {
            var info = Marshal.PtrToStructure<Kbdllhookstruct>(lParam);
            if (info.VkCode == VkCapital || info.VkCode == VkEisu)
            {
                var msg = unchecked((uint)(nint)wParam);
                // 一部配列/IME では wParam より flags(LLKHF_UP) の方が正確に KeyUp を示す。
                var keyUp = (info.Flags & LlkhfUp) != 0;
                if (!keyUp)
                {
                    if (msg == WmKeyup || msg == WmSyskeyup)
                        keyUp = true;
                    else if (msg == WmKeydown || msg == WmSyskeydown)
                        keyUp = false;
                }

                if (!keyUp)
                    Console.WriteLine($"[TRACE] CapsLock KeyDown captured (vk={info.VkCode})");
                else
                    Console.WriteLine($"[TRACE] CapsLock KeyUp captured (vk={info.VkCode})");

                if (!TrySendF13Transition(keyUp))
                    return CallNextHookEx(_hookId, nCode, wParam, lParam);

                return (IntPtr)1;
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private bool TrySendF13Transition(bool keyUp)
    {
        if (!keyUp)
        {
            if (_isF13Down)
                return true;

            if (!TrySendF13(keyUp: false))
                return false;

            _isF13Down = true;
            F13StateChanged?.Invoke(this, true);
            return true;
        }

        if (!_isF13Down)
            return true;

        if (!TrySendF13(keyUp: true))
            return false;

        _isF13Down = false;
        F13StateChanged?.Invoke(this, false);
        return true;
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

    private void EnsureF13Released()
    {
        if (!_isF13Down)
            return;

        if (TrySendF13(keyUp: true))
        {
            _isF13Down = false;
            F13StateChanged?.Invoke(this, false);
        }
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
