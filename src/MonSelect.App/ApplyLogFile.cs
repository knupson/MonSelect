using System.IO;
using MonSelect.Core.Engine;

namespace MonSelect.App;

/// <summary>
/// Vuelca las aplicaciones a un archivo por día y borra los viejos. El log en
/// memoria de ApplyLog alimenta la GUI de F2; éste es el que sobrevive a un
/// reinicio y sirve para entender por qué una app no obedeció ayer.
/// </summary>
public sealed class ApplyLogFile(int keepDays = 7)
{
    private readonly Lock _gate = new();

    public void Write(ApplyEntry entry)
    {
        var line = string.Join('\t',
            entry.At.ToString("O"),
            entry.Result,
            entry.RuleName ?? "-",
            entry.Attempts,
            entry.Title,
            entry.Detail ?? "-");

        lock (_gate)
        {
            Directory.CreateDirectory(ConfigPaths.LogDirectory);
            File.AppendAllText(PathForToday(), line + Environment.NewLine);
        }
    }

    private static string PathForToday()
        => Path.Combine(ConfigPaths.LogDirectory, $"monselect-{DateTime.Now:yyyy-MM-dd}.log");

    public void Prune()
    {
        if (!Directory.Exists(ConfigPaths.LogDirectory))
            return;

        var cutoff = DateTime.Now.AddDays(-keepDays);

        foreach (var file in Directory.EnumerateFiles(ConfigPaths.LogDirectory, "monselect-*.log"))
        {
            try
            {
                if (File.GetLastWriteTime(file) < cutoff)
                    File.Delete(file);
            }
            catch (IOException)
            {
                // Un archivo en uso se borra la próxima vez. No vale abortar por esto.
            }
        }
    }
}
