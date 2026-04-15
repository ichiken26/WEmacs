using System.Runtime.InteropServices;
using System.Threading;

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
    private const int UpDownMergeWindowMs = 8;
    private const int MissingKeyUpTimeoutMs = 40;

    private IntPtr _hookId = IntPtr.Zero;
    private readonly LowLevelKeyboardProc _proc;
    private readonly object _stateGate = new();
    private readonly Timer _deferredKeyUpTimer;
    private readonly Timer _missingKeyUpTimer;
    private bool _isF13Down;
    private bool _isCapsPhysicalDown;
    private bool _hasDeferredF13Up;

    public event EventHandler<bool>? F13StateChanged;

    public CapsLockKeyHook()
    {
        _proc = HookCallback;
        _deferredKeyUpTimer = new Timer(_ => FlushDeferredF13Up(), null, Timeout.Infinite, Timeout.Infinite);
        _missingKeyUpTimer = new Timer(_ => FlushMissingKeyUp(), null, Timeout.Infinite, Timeout.Infinite);
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
                lock (_stateGate)
                {
                    var msg = unchecked((uint)(nint)wParam);
                    var isInjected = (info.Flags & 0x10) != 0;
                    // 一部配列/IME では wParam より flags(LLKHF_UP) の方が正確に KeyUp を示す。
                    var keyUp = (info.Flags & LlkhfUp) != 0;
                    if (!keyUp)
                    {
                        if (msg == WmKeyup || msg == WmSyskeyup)
                            keyUp = true;
                        else if (msg == WmKeydown || msg == WmSyskeydown)
                            keyUp = false;
                    }

                    Console.WriteLine(
                        $"[TRACE] Caps event raw: vk={info.VkCode}, scan={info.ScanCode}, flags=0x{info.Flags:X}, msg=0x{msg:X}, up={keyUp}, injected={isInjected}, capsPhysicalDown={_isCapsPhysicalDown}, f13Down={_isF13Down}");

                    if (keyUp)
                    {
                        _missingKeyUpTimer.Change(Timeout.Infinite, Timeout.Infinite);

                        if (!_isCapsPhysicalDown)
                        {
                            Console.WriteLine("[TRACE] Ignored CapsLock KeyUp (received before KeyDown).");
                            return (IntPtr)1;
                        }

                        _isCapsPhysicalDown = false;
                        Console.WriteLine($"[TRACE] CapsLock KeyUp captured (vk={info.VkCode})");

                        if (_isF13Down)
                        {
                            _hasDeferredF13Up = true;
                            _deferredKeyUpTimer.Change(UpDownMergeWindowMs, Timeout.Infinite);
                            Console.WriteLine($"[TRACE] Deferred F13 KeyUp ({UpDownMergeWindowMs}ms window).");
                            return (IntPtr)1;
                        }
                    }
                    else
                    {
                        _missingKeyUpTimer.Change(MissingKeyUpTimeoutMs, Timeout.Infinite);

                        if (_hasDeferredF13Up)
                        {
                            // KeyDown が続く間は KeyUp を保留し続ける。8ms 静かになった時点で KeyUp を流す。
                            _isCapsPhysicalDown = true;
                            _deferredKeyUpTimer.Change(UpDownMergeWindowMs, Timeout.Infinite);
                            Console.WriteLine($"[TRACE] Extended deferred F13 KeyUp by {UpDownMergeWindowMs}ms (KeyDown still arriving).");
                            return (IntPtr)1;
                        }

                        if (_isCapsPhysicalDown)
                        {
                            Console.WriteLine("[TRACE] Ignored repeated CapsLock KeyDown.");
                            return (IntPtr)1;
                        }

                        _isCapsPhysicalDown = true;
                        Console.WriteLine($"[TRACE] CapsLock KeyDown captured (vk={info.VkCode})");
                    }

                    if (!TrySendF13Transition(keyUp))
                    {
                        Console.WriteLine($"[TRACE] F13 send failed (lastError={Marshal.GetLastWin32Error()})");
                        return CallNextHookEx(_hookId, nCode, wParam, lParam);
                    }

                    return (IntPtr)1;
                }
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

            Console.WriteLine("[TRACE] F13 KeyDown recognized (injected)");
            _isF13Down = true;
            F13StateChanged?.Invoke(this, true);
            return true;
        }

        if (!_isF13Down)
            return true;

        if (!TrySendF13(keyUp: true))
            return false;

        Console.WriteLine("[TRACE] F13 KeyUp recognized (injected)");
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

        var input = new Input
        {
            Type = InputKeyboard,
            Union = new InputUnion { Keyboard = ki }
        };
        return SendInput(1, [input], Marshal.SizeOf<Input>()) == 1;
    }

    private void EnsureF13Released()
    {
        lock (_stateGate)
        {
            _hasDeferredF13Up = false;
            _deferredKeyUpTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _missingKeyUpTimer.Change(Timeout.Infinite, Timeout.Infinite);

            if (!_isF13Down)
                return;

            if (TrySendF13(keyUp: true))
            {
                _isF13Down = false;
                _isCapsPhysicalDown = false;
                F13StateChanged?.Invoke(this, false);
            }
        }
    }

    private void FlushDeferredF13Up()
    {
        lock (_stateGate)
        {
            if (!_hasDeferredF13Up)
                return;

            _hasDeferredF13Up = false;
            if (!_isF13Down)
                return;

            if (!TrySendF13(keyUp: true))
            {
                Console.WriteLine($"[TRACE] Deferred F13 KeyUp failed (lastError={Marshal.GetLastWin32Error()})");
                return;
            }

            Console.WriteLine("[TRACE] F13 KeyUp recognized (injected/deferred)");
            _isF13Down = false;
            _isCapsPhysicalDown = false;
            F13StateChanged?.Invoke(this, false);
        }
    }

    private void FlushMissingKeyUp()
    {
        lock (_stateGate)
        {
            if (!_isF13Down || !_isCapsPhysicalDown)
                return;

            _hasDeferredF13Up = false;
            _deferredKeyUpTimer.Change(Timeout.Infinite, Timeout.Infinite);

            if (!TrySendF13(keyUp: true))
            {
                Console.WriteLine($"[TRACE] Missing-KeyUp fallback failed (lastError={Marshal.GetLastWin32Error()})");
                return;
            }

            Console.WriteLine($"[TRACE] Missing-KeyUp fallback fired ({MissingKeyUpTimeoutMs}ms): forced F13 KeyUp.");
            _isF13Down = false;
            _isCapsPhysicalDown = false;
            F13StateChanged?.Invoke(this, false);
        }
    }

    public void Dispose()
    {
        _missingKeyUpTimer.Dispose();
        _deferredKeyUpTimer.Dispose();
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
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public Mouseinput Mouse;
        [FieldOffset(0)] public Keybdinput Keyboard;
        [FieldOffset(0)] public Hardwareinput Hardware;
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
