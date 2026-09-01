using System.Text.RegularExpressions;
using MonSelect.Core.Windows;

namespace MonSelect.Core.Rules;

/// <summary>
/// Decide qué regla gobierna una ventana. Puro y sin estado: es la pieza que
/// más se testea porque es donde el usuario va a discutir el comportamiento.
/// </summary>
public static class RuleMatcher
{
    /// <summary>
    /// Primera regla habilitada que matchea, en el orden del archivo. No hay
    /// scoring por especificidad: con veinte reglas, un ganador impredecible es
    /// imposible de depurar.
    /// </summary>
    public static Rule? FirstMatch(IReadOnlyList<Rule> rules, WindowInfo window)
    {
        foreach (var rule in rules)
        {
            if (rule.Enabled && Matches(rule, window))
                return rule;
        }

        return null;
    }

    public static bool Matches(Rule rule, WindowInfo window)
    {
        var c = rule.Match;

        if (c.Exe is not null && !ExeMatches(c.Exe, window.ExePath))
            return false;

        if (c.CommandLine is not null && !TextMatches(c.CommandLine, window.CommandLine))
            return false;

        if (c.ClassName is not null
            && !string.Equals(c.ClassName, window.ClassName, StringComparison.Ordinal))
            return false;

        if (c.Title is not null && !RegexMatches(c.Title, window.Title))
            return false;

        if (c.Aumid is not null
            && !string.Equals(c.Aumid, window.Aumid, StringComparison.Ordinal))
            return false;

        return true;
    }

    /// <summary>Compara paths normalizando separadores y sin distinguir mayúsculas.</summary>
    private static bool ExeMatches(string pattern, string? actual)
    {
        if (actual is null)
            return false;

        return string.Equals(Normalise(pattern), Normalise(actual), StringComparison.OrdinalIgnoreCase);

        static string Normalise(string path) => path.Replace('/', '\\').TrimEnd('\\');
    }

    /// <summary>Substring por defecto; regex si el patrón viene envuelto entre barras.</summary>
    private static bool TextMatches(string pattern, string? actual)
    {
        if (actual is null)
            return false;

        if (pattern.Length >= 2 && pattern[0] == '/' && pattern[^1] == '/')
            return RegexMatches(pattern[1..^1], actual);

        return actual.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Un regex inválido no matchea en vez de tirar excepción: la config la
    /// escribe una persona a mano y no puede tumbar el servicio.
    /// </summary>
    private static bool RegexMatches(string pattern, string? actual)
    {
        if (actual is null)
            return false;

        try
        {
            return Regex.IsMatch(actual, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
