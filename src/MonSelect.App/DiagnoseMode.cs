using MonSelect.Core.Engine;
using MonSelect.Core.Windows;

namespace MonSelect.App;

/// <summary>
/// Vuelca cada ventana que aparece con todos sus campos de matcheo. Es la
/// herramienta con la que se escriben reglas para aplicaciones difíciles.
/// </summary>
public static class DiagnoseMode
{
    public static int Run()
    {
        using var watcher = new WindowWatcher();
        var probe = new WindowProbe(new Win32WindowSystem());

        watcher.WindowAppeared += hwnd =>
        {
            var info = probe.Describe(hwnd);
            if (info is null || string.IsNullOrEmpty(info.Title))
                return;

            Console.WriteLine(new string('-', 70));
            Console.WriteLine($"title   : {info.Title}");
            Console.WriteLine($"exe     : {info.ExePath ?? "<sin acceso>"}");
            Console.WriteLine($"cmdline : {info.CommandLine ?? "<sin acceso>"}");
            Console.WriteLine($"class   : {info.ClassName}");
            Console.WriteLine($"state   : {info.CurrentState}");
            Console.WriteLine($"bounds  : {info.Bounds}");
        };

        watcher.Start();
        Console.WriteLine("MonSelect --diagnose. Abrí aplicaciones. Enter para salir.");
        Console.ReadLine();
        return 0;
    }
}
