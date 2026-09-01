using MonSelect.Core.Win32;

namespace MonSelect.Core.Monitors;

/// <summary>Qué hacer cuando el monitor que pide una regla no está conectado.</summary>
public enum IfMissing
{
    /// <summary>No aplicar la regla. Nunca colocar en un monitor equivocado.</summary>
    Skip,

    /// <summary>Caer al monitor principal.</summary>
    Primary,

    /// <summary>Caer al monitor más cercano a donde la ventana ya estaba.</summary>
    Nearest,
}

public sealed class MonitorRegistry(IMonitorSystem system)
{
    public IReadOnlyList<MonitorInfo> Monitors => system.GetMonitors();

    public MonitorInfo? Primary()
        => system.GetMonitors().FirstOrDefault(m => m.IsPrimary);

    /// <param name="fallbackAnchor">
    /// Posición actual de la ventana, usada sólo por <see cref="IfMissing.Nearest"/>.
    /// </param>
    public MonitorInfo? Resolve(MonitorId id, IfMissing policy, Rect fallbackAnchor)
    {
        var monitors = system.GetMonitors();

        var exact = monitors.FirstOrDefault(m => m.Id == id);
        if (exact is not null)
            return exact;

        return policy switch
        {
            IfMissing.Skip => null,
            IfMissing.Primary => Primary(),
            IfMissing.Nearest => Nearest(monitors, fallbackAnchor),
            _ => null,
        };
    }

    private static MonitorInfo? Nearest(IReadOnlyList<MonitorInfo> monitors, Rect anchor)
    {
        if (monitors.Count == 0)
            return null;

        var ax = anchor.Left + anchor.Width / 2.0;
        var ay = anchor.Top + anchor.Height / 2.0;

        return monitors
            .OrderBy(m =>
            {
                var mx = m.Bounds.Left + m.Bounds.Width / 2.0;
                var my = m.Bounds.Top + m.Bounds.Height / 2.0;
                var dx = mx - ax;
                var dy = my - ay;
                return dx * dx + dy * dy;
            })
            .First();
    }
}
