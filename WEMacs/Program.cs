using Gma.System.MouseKeyHook;
using System.Windows.Forms;

namespace WEMacs;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Console.WriteLine("--- Emacs Key Engine ---");

        var configPath = Path.Combine(AppContext.BaseDirectory, "keybindings.json");
        var bindings = KeyBindingConfig.Load(configPath);

        using var globalHook = Hook.GlobalEvents();
        using var capsLockHook = new CapsLockKeyHook();

        var isF13Pressed = false;
        var activeBindings = new HashSet<Keys>();

        capsLockHook.F13StateChanged += (_, isDown) =>
        {
            isF13Pressed = isDown;
            Console.WriteLine(isDown ? "--- F13 ON ---" : "--- F13 OFF ---");
        };

        capsLockHook.Install();

        globalHook.KeyDown += (_, e) =>
        {
            if (IsF13Like(e))
                Console.WriteLine($"[TRACE] F13 KeyDown recognized (keyCode={e.KeyCode}, keyValue={e.KeyValue})");

            if (!isF13Pressed)
                return;

            if (!bindings.TryGetValue(e.KeyCode, out var binding))
                return;

            e.Handled = true;
            if (!activeBindings.Add(e.KeyCode))
                return;

            Console.WriteLine(binding.Log);
            VirtualKeySender.SendKeyDown(binding.SendKeys);
        };

        globalHook.KeyUp += (_, e) =>
        {
            if (IsF13Like(e))
                Console.WriteLine($"[TRACE] F13 KeyUp recognized (keyCode={e.KeyCode}, keyValue={e.KeyValue})");

            if (!activeBindings.Remove(e.KeyCode))
                return;

            if (!bindings.TryGetValue(e.KeyCode, out var binding))
                return;

            e.Handled = true;
            VirtualKeySender.SendKeyUp(binding.SendKeys);
        };

        Application.Run();
    }

    private static bool IsF13Like(KeyEventArgs e)
    {
        return e.KeyCode == Keys.F13 || e.KeyValue == 124 || e.KeyValue == 240 || e.KeyValue == 20;
    }
}
