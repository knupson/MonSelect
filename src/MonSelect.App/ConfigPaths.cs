using System.IO;

namespace MonSelect.App;

public static class ConfigPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MonSelect");

    public static string Rules => Path.Combine(Root, "rules.yaml");
    public static string Borderless => Path.Combine(Root, "borderless.json");
    public static string LogDirectory => Path.Combine(Root, "logs");
}
