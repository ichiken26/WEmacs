using System.Runtime.InteropServices;

namespace WEMacs;

/// <summary>
/// Win32 の低レベルキーボードフック (WH_KEYBOARD_LL)。
/// 同一スレッドでメッセージループ (GetMessage など) を回す必要があります。
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeydown = 0x0100;
    private const int WmKeyup = 0x0101;
    private const int WmSyskeydown = 0x0104;
    private const int WmSyskeyup = 0x0105;
    private const int HcAction = 0;

    private IntPtr _hookId = IntPtr.Zero;
    private readonly LowLevelKeyboardProc _proc;

    public event EventHandler<KeyboardHookEventArgs>? KeyDown;
    public event EventHandler<KeyboardHookEventArgs>? KeyUp;

    public KeyboardHook()
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
            var vk = (int)info.VkCode;
            var isSys = wParam == (IntPtr)WmSyskeydown || wParam == (IntPtr)WmSyskeyup;

            if (wParam == (IntPtr)WmKeydown || wParam == (IntPtr)WmSyskeydown)
                KeyDown?.Invoke(this, new KeyboardHookEventArgs(vk, info.ScanCode, info.Flags, isSys, isKeyUp: false));
            else if (wParam == (IntPtr)WmKeyup || wParam == (IntPtr)WmSyskeyup)
                KeyUp?.Invoke(this, new KeyboardHookEventArgs(vk, info.ScanCode, info.Flags, isSys, isKeyUp: true));
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        Uninstall();
        GC.SuppressFinalize(this);
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Kbdllhookstruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
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

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}

public sealed class KeyboardHookEventArgs : EventArgs
{
    public int VirtualKeyCode { get; }
    public uint ScanCode { get; }
    public uint Flags { get; }
    public bool IsSystemKey { get; }
    public bool IsKeyUp { get; }

    public KeyboardHookEventArgs(int virtualKeyCode, uint scanCode, uint flags, bool isSystemKey, bool isKeyUp)
    {
        VirtualKeyCode = virtualKeyCode;
        ScanCode = scanCode;
        Flags = flags;
        IsSystemKey = isSystemKey;
        IsKeyUp = isKeyUp;
    }
}
