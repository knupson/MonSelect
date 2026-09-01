using MonSelect.Core.Rules;

namespace MonSelect.App;

/// <summary>Una fila de la tabla "Reglas". El orden en la lista ES la prioridad.</summary>
internal sealed class RuleRow
{
    public required Rule Rule { get; init; }
    public required string MatchSummary { get; init; }
    public required string Destination { get; init; }
    public required string StateLabel { get; init; }

    public static RuleRow From(Rule rule)
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
        };
    }
}
