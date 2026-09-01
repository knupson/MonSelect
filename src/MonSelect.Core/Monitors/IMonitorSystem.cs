using MonSelect.Core.Win32;

namespace MonSelect.Core.Monitors;

/// <summary>Frontera hacia el subsistema de display. Los tests la sustituyen.</summary>
public interface IMonitorSystem
{
    IReadOnlyList<MonitorInfo> GetMonitors();

    /// <summary>Monitor que contiene el rect, o null si no lo toca ninguno.</summary>
    MonitorInfo? GetMonitorForRect(Rect rect);
}
