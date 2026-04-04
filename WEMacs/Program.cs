// using Gma.System.MouseKeyHook;
// using System.Windows.Forms;
// using WEMacs;

// Console.WriteLine("--- Emacs Key Engine 起動中 ---");
// Console.WriteLine("Caps Lock を押すとアプリ内で F13 に変換して送ります（SharpKeys は不要）。");
// Console.WriteLine("終了はウィンドウを閉じるかプロセスを止めてください。");

// // グローバル LL フックは「後から登録したもの」が先に呼ばれる。Caps を先に奪うため Remapper は最後に Install。
// var m_GlobalHook = Hook.GlobalEvents();

// using var capsToF13 = new CapsLockToF13Remapper();
// capsToF13.Install();

// m_GlobalHook.KeyDown += (_, e) =>
// {
//     if (e.KeyCode == Keys.F13)
//         Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] F13 を検知（物理キーは Caps Lock）");
// };

// Application.Run();

using Gma.System.MouseKeyHook;
using System.Windows.Forms;

Console.WriteLine("--- Emacs Key Engine: 240検知モード ---");

var m_GlobalHook = Hook.GlobalEvents();

// F13（CapsLock相当）が押されているかどうかのフラグ
bool isF13Pressed = false;

m_GlobalHook.KeyDown += (s, e) =>
{
    // 240 (英数) または 20 (CapsLock) を検知
    if (e.KeyValue == 240 || e.KeyValue == 20)
    {
        e.Handled = true; // OSにCapsLockを無効化
        // すでにONなら、ログを出さずにリターンする（ここがポイント！）
        if (isF13Pressed) return; 

        isF13Pressed = true;
        e.Handled = true;
        Console.WriteLine("--- CapsLock (F13モード) ON ---");
    }

    // F13を押しながら P を押した時の処理
    if (isF13Pressed && e.KeyCode == Keys.P)
    {
        Console.WriteLine("Emacs Move: Up (↑)");
        SendKeys.SendWait("{UP}"); // 上矢印を送信
        e.Handled = true; // P が入力されるのを防ぐ
    }
};

m_GlobalHook.KeyUp += (s, e) =>
{
    if (e.KeyValue == 240 || e.KeyValue == 20)
    {
        isF13Pressed = false;
        Console.WriteLine("--- CapsLock (F13モード) OFF ---");
    }
};

Application.Run();