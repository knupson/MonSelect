using MonSelect.Core.Win32;

namespace MonSelect.Core.Monitors;

/// <param name="Bounds">Rect completo del monitor. Es el destino de Borderless.</param>
/// <param name="WorkArea">Rect sin taskbar ni appbars. Es el destino de Maximized.</param>
public sealed record MonitorInfo(
    MonitorId Id,
    string GdiName,
    Rect Bounds,
    Rect WorkArea,
    bool IsPrimary);
