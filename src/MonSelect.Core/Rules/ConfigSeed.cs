using MonSelect.Core.Monitors;

namespace MonSelect.Core.Rules;

/// <summary>
/// Genera el bloque monitors: del primer arranque. El usuario no tiene por qué
/// escribir un device path a mano; renombra los alias y listo.
/// </summary>
public static class ConfigSeed
{
    public static RuleSet Seed(IReadOnlyList<MonitorInfo> monitors)
    {
        var aliases = new Dictionary<string, MonitorAlias>(StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var monitor in monitors)
        {
            var alias = Unique(BaseAlias(monitor), used);
            used.Add(alias);
            aliases[alias] = new MonitorAlias(monitor.Id.DevicePath, Label(monitor));
        }

        return new RuleSet(1, aliases, Array.Empty<Rule>());
    }

    private static string BaseAlias(MonitorInfo monitor)
    {
        if (monitor.IsPrimary)
            return "primary";

        // \\.\DISPLAY3 -> display3
        var digits = new string(monitor.GdiName.Where(char.IsDigit).ToArray());
        return digits.Length > 0 ? $"display{digits}" : "monitor";
    }

    private static string Unique(string basis, HashSet<string> used)
    {
        if (!used.Contains(basis))
            return basis;

        for (var i = 2; ; i++)
        {
            var candidate = $"{basis}{i}";
            if (!used.Contains(candidate))
                return candidate;
        }
    }

    /// <summary>
    /// Puramente cosmético: nada matchea contra esto. \\.\DISPLAY1 se escribe
    /// como DISPLAY1 para que, al lado del path entre comillas simples, no
    /// desentone con una fila de barras invertidas escapadas.
    /// </summary>
    private static string Label(MonitorInfo monitor)
    {
        var name = monitor.GdiName.TrimStart('\\', '.');
        return $"{name} {monitor.Bounds.Width}x{monitor.Bounds.Height}"
               + (monitor.IsPrimary ? " (principal)" : string.Empty);
    }
}
