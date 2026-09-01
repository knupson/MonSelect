using MonSelect.Core.Rules;
using MonSelect.Core.Windows;

namespace MonSelect.App;

/// <summary>Una fila de la tabla "Reglas". El orden en la lista ES la prioridad.</summary>
internal sealed class RuleRow
{
    public required Rule Rule { get; init; }
    public required string MatchSummary { get; init; }
    public required string Destination { get; init; }
    public required string StateLabel { get; init; }

    /// <summary>
    /// Cuántas ventanas abiertas AHORA MISMO matchean los criterios de esta
    /// regla — el mismo chequeo en vivo que ya existía en RuleEditorDialog,
    /// pero visible para una regla ya guardada sin tener que abrir "Editar".
    /// Una regla capturada con un criterio que dejó de matchear (p.ej. un
    /// título que cambió) da 0 acá en vez de descubrirse recién la próxima
    /// vez que se reabra la ventana.
    /// </summary>
    public required int MatchCount { get; init; }

    public string MatchCountLabel => MatchCount == 0 ? "0 — no matchea ninguna" : MatchCount.ToString();

    public static RuleRow From(Rule rule, IReadOnlyList<WindowInfo>? openWindows = null)
    {
        var parts = new List<string>();
        if (rule.Match.Exe is { } exe) parts.Add($"exe: {System.IO.Path.GetFileName(exe)}");
        if (rule.Match.ClassName is { } cls) parts.Add($"class: {cls}");
        if (rule.Match.CommandLine is { } cmd) parts.Add($"cmdline: {cmd}");
        if (rule.Match.Title is { } title) parts.Add($"title: {title}");
        if (rule.Match.Aumid is { } aumid) parts.Add($"aumid: {aumid}");

        return new RuleRow
        {
            Rule = rule,
            MatchSummary = parts.Count > 0 ? string.Join("  ·  ", parts) : "(cualquier ventana)",
            Destination = rule.Place.MonitorAliases.Count > 0
                ? string.Join(" → ", rule.Place.MonitorAliases)
                : "(sin monitor)",
            StateLabel = rule.Place.State.ToString(),
            MatchCount = openWindows is null ? 0 : CountMatches(rule, openWindows),
        };
    }

    /// <summary>
    /// RuleMatcher no tira por criterios raros, pero se envuelve igual: una
    /// regla a medio editar en el archivo no puede tumbar esta columna.
    /// </summary>
    private static int CountMatches(Rule rule, IReadOnlyList<WindowInfo> openWindows)
    {
        var count = 0;
        foreach (var window in openWindows)
        {
            try
            {
                if (RuleMatcher.Matches(rule, window))
                    count++;
            }
            catch
            {
                // Se ignora: no matchea, no tumba el resto de la tabla.
            }
        }

        return count;
    }
}
