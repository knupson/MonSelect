namespace MonSelect.Core.Rules;

public sealed record MonitorAlias(string Path, string Label);

public sealed record RuleSet(
    int Version,
    IReadOnlyDictionary<string, MonitorAlias> Monitors,
    IReadOnlyList<Rule> Rules)
{
    public static readonly RuleSet Empty = new(
        1,
        new Dictionary<string, MonitorAlias>(),
        Array.Empty<Rule>());
}

/// <summary>Config ilegible. El mensaje va directo al tray, así que tiene que ser humano.</summary>
public sealed class RuleSetFormatException(string message, Exception? inner = null)
    : Exception(message, inner);
