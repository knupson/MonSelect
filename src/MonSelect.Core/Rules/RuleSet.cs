using MonSelect.Core.Monitors;

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

    /// <summary>
    /// Alias declarado en el bloque monitors: para este monitor, o null si el
    /// monitor no tiene alias en la config actual. Lo usa la GUI para mostrar y
    /// para armar una regla nueva a partir de una ventana ya colocada a mano.
    /// </summary>
    public string? AliasFor(MonitorId id)
    {
        foreach (var (alias, monitor) in Monitors)
        {
            if (string.Equals(monitor.Path, id.DevicePath, StringComparison.OrdinalIgnoreCase))
                return alias;
        }

        return null;
    }
}

/// <summary>Config ilegible. El mensaje va directo al tray, así que tiene que ser humano.</summary>
public sealed class RuleSetFormatException(string message, Exception? inner = null)
    : Exception(message, inner);
