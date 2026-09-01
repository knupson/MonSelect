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

        if (args.Contains("--diagnose"))
            return DiagnoseMode.Run();

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        using var bootstrap = new Bootstrap();
        bootstrap.Start();
        using var tray = new TrayHost(bootstrap);

        return app.Run();
    }
}
