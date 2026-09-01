using System.Diagnostics;

namespace MonSelect.App;

/// <summary>
/// Registra MonSelect como tarea at-logon con privilegios máximos. No se usa la
/// carpeta Startup ni la clave Run porque sin elevación no se pueden manipular
/// ventanas de aplicaciones elevadas.
/// </summary>
public static class Autostart
{
    private const string TaskName = "MonSelect";

    public static bool Install()
    {
        var exe = Environment.ProcessPath;
        if (exe is null)
            return false;

        return Run($"/Create /TN {TaskName} /TR \"\\\"{exe}\\\"\" /SC ONLOGON /RL HIGHEST /F");
    }

    public static bool Uninstall() => Run($"/Delete /TN {TaskName} /F");

    private static bool Run(string arguments)
    {
        var process = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });

        if (process is null)
            return false;

        process.WaitForExit();

        if (process.ExitCode != 0)
            Console.Error.WriteLine(process.StandardError.ReadToEnd());

        return process.ExitCode == 0;
    }
}
