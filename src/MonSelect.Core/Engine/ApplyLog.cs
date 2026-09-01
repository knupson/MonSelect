namespace MonSelect.Core.Engine;

public enum ApplyResult
{
    /// <summary>Ninguna regla matcheó. La ventana queda donde Windows la puso.</summary>
    NoMatch,

    /// <summary>Matcheó, pero el monitor no está y la política dijo que no la toque.</summary>
    Skipped,

    /// <summary>Colocada donde correspondía.</summary>
    Applied,

    /// <summary>Se agotó el presupuesto de reintentos y la ventana se quedó en otro lado.</summary>
    Resisted,

    /// <summary>Matcheó pero el modo apply decidió no tocarla, como First con una segunda ventana.</summary>
    Ignored,
}

public sealed record ApplyEntry(
    DateTimeOffset At,
    nint Handle,
    string Title,
    string? RuleName,
    ApplyResult Result,
    int Attempts,
    string? Detail);

/// <summary>Buffer circular de las últimas aplicaciones. Es lo que se ve en la GUI de F2.</summary>
public sealed class ApplyLog(int capacity = 200)
{
    private readonly Queue<ApplyEntry> _entries = new();
    private readonly Lock _gate = new();

    /// <summary>
    /// Se dispara por cada entrada. Los consumidores que persisten a disco deben
    /// engancharse acá: comparar Count antes y después no sirve, porque este es
    /// un buffer circular y su Count deja de crecer al llegar a la capacidad.
    /// </summary>
    public event Action<ApplyEntry>? EntryAdded;

    public void Add(ApplyEntry entry)
    {
        lock (_gate)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > capacity)
                _entries.Dequeue();
        }

        EntryAdded?.Invoke(entry);
    }

    public IReadOnlyList<ApplyEntry> Recent()
    {
        lock (_gate)
            return _entries.ToArray();
    }
}
