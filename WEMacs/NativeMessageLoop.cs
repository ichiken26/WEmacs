using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WEMacs;

/// <summary>
/// 低レベルフックが動作するために必要な Win32 メッセージループ。
/// </summary>
public static class NativeMessageLoop
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public POINT Pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    public static void Run()
    {
        MSG msg;
        int r;
        while ((r = GetMessage(out msg, IntPtr.Zero, 0, 0)) != 0)
        {
            if (r == -1)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern void PostQuitMessage(int nExitCode);
}
