using System.Windows;
using Application = System.Windows.Application;

namespace MonSelect.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--install-autostart"))
            return Autostart.Install() ? 0 : 1;

        if (args.Contains("--uninstall-autostart"))
            return Autostart.Uninstall() ? 0 : 1;

        if (args.Contains("--apply-now"))
            return ApplyNow();

        if (args.Contains("--diagnose"))
            return DiagnoseMode.Run();

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        using var bootstrap = new Bootstrap();
        bootstrap.Start();
        using var tray = new TrayHost(bootstrap);

        // --gui abre la ventana al arrancar, sin tener que encontrar el icono
        // en la bandeja. La app sigue residente igual al cerrarla.
        if (args.Contains("--gui"))
            tray.OpenGuiWindow();

        return app.Run();
    }

    /// <summary>
    /// Reaplica las reglas a las ventanas ya abiertas y termina. Es el mismo
    /// comando del menú de bandeja, accesible sin ratón — útil para verificar
    /// un cambio de reglas sin cerrar y reabrir cada aplicación.
    /// </summary>
    private static int ApplyNow()
    {
        using var bootstrap = new Bootstrap();
        bootstrap.StartForOneShot();

        var handles = TopLevelWindows.Enumerate().ToList();
        bootstrap.Engine.ApplyAllAsync(handles, CancellationToken.None)
            .GetAwaiter().GetResult();

        Console.WriteLine($"reglas aplicadas a {handles.Count} ventanas");
        return 0;
    }
}
